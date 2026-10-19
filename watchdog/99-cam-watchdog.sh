#!/bin/sh
# boot hook: start cam-watchdog after networking/mongo are up
sleep 30
( /data/scripts/cam-watchdog.sh >/dev/null 2>&1 & )
