#!/bin/sh
# UDM watchdog: camera link-lost + evil-twin + threat events (ace.alert) -> ntfy
CONF=/data/scripts/cam-watchdog.conf
LOG=/data/logs/cam-watchdog.log
PIDF=/run/cam-watchdog.pid

log(){ echo "$(date '+%F %T') $*" >> "$LOG"; }

if [ -f "$PIDF" ]; then
  oldpid=$(cat "$PIDF" 2>/dev/null)
  [ -n "$oldpid" ] && [ -d "/proc/$oldpid" ] && { log "already running pid $oldpid, exiting"; exit 0; }
fi
echo $$ > "$PIDF"

notify(){
  # $1 title  $2 body  $3 ntfy priority
  if curl -s -m 8 -H "Title: $1" -H "Priority: $3" -H "Tags: warning" -d "$2" "$NTFY_URL" >/dev/null 2>&1; then
    log "PUSH OK: $1"
  else
    log "PUSH FAIL: $1"
  fi
}

NTFY_URL="unset"
IPS_WATCH_KEYS="IPS|HONEYPOT|THREAT"
. "$CONF"
log "=== watchdog started pid $$ (multi-cam + alert) ==="
notify "Watchdog started" "pid $$; cameras: ${CAMS:-none}; threat-watch: ${IPS_WATCH_KEYS}" low

last_hb=$(date +%s)
rogue_cycles=0
last_ips_time=""
last_wpa_bad=""
last_wpa_alert=0

cam_check(){
  ip_=$1; dev_=$2
  eval "st=\$camstate_$ip_ ; ms=\$cammiss_$ip_ ; ds=\$camdown_$ip_ ; lr=\$camrealert_$ip_"
  [ -z "$st" ] && { st=init; ms=0; ds=0; lr=0; }
  now_=$(date +%s)
  if ip neigh show dev "$dev_" 2>/dev/null | grep -q "^$ip_ "; then
    if [ "$st" = "down" ]; then
      notify "Camera link restored" "$ip_ back on $dev_ after $((now_ - ds))s" default
      log "camera $ip_ UP"
    elif [ "$st" = "init" ]; then
      log "camera first seen UP: $ip_ on $dev_"
    fi
    st=up; ms=0
  else
    ms=$((ms + 1))
    if [ "$ms" -ge "$OFFLINE_MISS_LIMIT" ]; then
      if [ "$st" != "down" ]; then
        st=down; ds=$now_; lr=$now_
        notify "CAMERA LINK LOST" "$ip_ vanished from $dev_ neigh table (${ms}x${POLL_SECS}s)" high
        log "camera $ip_ DOWN"
      elif [ $((now_ - lr)) -ge "$REALERT_SECS" ]; then
        lr=$now_
        notify "Camera still offline" "$ip_ down for $((now_ - ds))s" high
      fi
    fi
  fi
  eval "camstate_$ip_=\$st; cammiss_$ip_=\$ms; camdown_$ip_=\$ds; camrealert_$ip_=\$lr"
}

while :; do
  . "$CONF"
  now=$(date +%s)

  for entry in $CAMS; do
    cip=${entry%@*}; cdev=${entry#*@}
    [ -n "$cip" ] && [ -n "$cdev" ] && cam_check "$cip" "$cdev"
  done

  rogue_cycles=$((rogue_cycles + 1))
  if [ $((rogue_cycles * POLL_SECS)) -ge "$ROGUE_POLL_SECS" ]; then
    rogue_cycles=0

    # ---- evil twin: unknown AP broadcasting one of OUR ssids ----
    ssids_json=$(printf '%s\n' $WATCH_SSIDS | awk '{printf "%s\"%s\"", (NR>1?",":""), $0} END{print ""}')
    out=$(timeout 15 mongo --quiet 127.0.0.1:27117/ace --eval "db.rogue.find({essid:{\$in:[$ssids_json]},is_known:{\$ne:true}},{essid:1,bssid:1}).limit(5).forEach(function(r){print(r.bssid+'|'+r.essid)})" 2>/dev/null)
    [ -n "$out" ] && echo "$out" | while IFS='|' read -r bssid essid; do
      [ -z "$bssid" ] && continue
      seen=/tmp/rogue-$bssid
      last=$(cat "$seen" 2>/dev/null || echo 0)
      if [ $((now - last)) -ge "$ROGUE_REALERT_SECS" ]; then
        echo "$now" > "$seen"
        notify "EVIL TWIN SUSPECTED" "Unknown AP $bssid is broadcasting your SSID '$essid'" high
        log "rogue: $bssid essid=$essid"
      fi
    done

    # ---- threat events: new ace.alert docs matching key pattern ----
    if [ "${IPS_ENABLED:-1}" = "1" ]; then
      if [ -z "$last_ips_time" ]; then
        last_ips_time=$(timeout 15 mongo --quiet 127.0.0.1:27117/ace --eval "var m=db.alert.find({key:{\$regex:/$IPS_WATCH_KEYS/}}).sort({time:-1}).limit(1).toArray(); print(m.length? m[0].time : Date.now())" 2>/dev/null | tr -d "\r\n ")
        case "$last_ips_time" in ''|*[!0-9]*) last_ips_time="$(date +%s)000";; esac
        log "threat-watch baseline: $last_ips_time"
      else
        out3=$(timeout 15 mongo --quiet 127.0.0.1:27117/ace --eval "var q={key:{\$regex:/$IPS_WATCH_KEYS/},time:{\$gt:NumberLong($last_ips_time)}}; var c=db.alert.find(q).sort({time:1}).limit(4); c.forEach(function(e){print(e.key+'|'+((e.msg||e.message||e.subsystem||'').toString().substring(0,90)))}); var m=db.alert.find(q).sort({time:-1}).limit(1).toArray(); if(m.length){print('T:'+m[0].time)}" 2>/dev/null)
        if [ -n "$out3" ]; then
          newt=$(echo "$out3" | grep "^T:" | cut -d: -f2 | tr -d "\r\n ")
          lines=$(echo "$out3" | grep -v "^T:")
          n=$(echo "$lines" | grep -c .)
          if [ "$n" -gt 0 ]; then
            first=$(echo "$lines" | head -1)
            notify "THREAT EVENT (${n})" "$first" high
            echo "$lines" | head -3 | while IFS='|' read -r k m2; do log "threat: $k $m2"; done
          fi
          [ -n "$newt" ] && last_ips_time=$newt
        fi
      fi
    fi

    # ---- wifi security drift: user SSIDs not in expected wpa mode ----
    if [ "${WPA_WATCH_ENABLED:-1}" = "1" ]; then
      allow_js=false; [ "${WPA_ALLOW_TRANSITION:-1}" = "1" ] && allow_js=true
      bad=$(timeout 15 mongo --quiet 127.0.0.1:27117/ace --eval "db.wlanconf.find({enabled:true,attr_hidden_id:{\$exists:false}},{name:1,wpa_mode:1,wpa3_transition:1,_id:0}).forEach(function(w){var ok=(w.wpa_mode==='$WPA_EXPECTED_MODE')||($allow_js && w.wpa3_transition===true); if(!ok){print(w.name+' ('+w.wpa_mode+')')}})" 2>/dev/null)
      if [ -n "$bad" ]; then
        now2=$(date +%s)
        if [ "$bad" != "$last_wpa_bad" ] || [ $((now2 - last_wpa_alert)) -ge "$ROGUE_REALERT_SECS" ]; then
          n2=$(echo "$bad" | grep -c .)
          names=$(echo "$bad" | head -6 | tr '\n' '; ')
          notify "WIFI SECURITY DEGRADED (${n2})" "$names" high
          log "wpa-drift: $(echo "$bad" | tr '\n' ' ')"
          last_wpa_bad=$bad
          last_wpa_alert=$now2
        fi
      else
        if [ -n "$last_wpa_bad" ]; then
          notify "WiFi security restored" "all user SSIDs in $WPA_EXPECTED_MODE (or allowed transition)" default
          log "wpa-drift cleared"
        fi
        last_wpa_bad=""
        last_wpa_alert=0
      fi
    fi
  fi

  if [ $((now - last_hb)) -ge "$HEARTBEAT_SECS" ]; then
    last_hb=$now
    notify "Watchdog heartbeat" "alive pid $$; cameras: ${CAMS:-none}" low
  fi

  sleep "$POLL_SECS"
done
