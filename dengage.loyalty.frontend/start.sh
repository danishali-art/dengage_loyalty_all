#!/bin/sh

# Same start-up as the dEngage panel frontend (adminpanel/start.sh): settings come from the
# environment, nginx.conf is rendered from nginx.conf.tpl, and nginx is watched and its logs rotated.
#
# Environment Variables
# LISTEN_PORT, BACKEND_URL, HOST_NAME_LIST, WORKING_DIRECTORY, OUTPUT_DIRECTORY, LOG_DIRECTORY
#
# Optional mounted files (/dengage/config):
#   loyalty-frontend.app-config.json      replaces the portal's app-config.json (feature flags, page sizes)
#   loyalty-frontend.server-extra.conf    included in the server block
#   loyalty-frontend.http-extra.conf      included in the http block
#   loyalty-frontend.global-extra.conf    included at the top level
# Optional start-up script: /dengage/startup-scripts/loyalty-frontend.sh

export LISTEN_PORT="${LISTEN_PORT:-8080}"

# The loyalty Api (dengage.loyalty.api); /api and /ping are forwarded to it.
export BACKEND_URL="${BACKEND_URL:-http://127.0.0.1:8080}" #set default value
export BACKEND_URL="${BACKEND_URL%/}" #trim the trailing slash

export WORKING_DIRECTORY="${WORKING_DIRECTORY:-/dengage/workdir/dengage.loyalty.frontend}" #set default value
export WORKING_DIRECTORY="${WORKING_DIRECTORY%/}" #trim the trailing slash
export LOG_DIRECTORY="${LOG_DIRECTORY:-/dengage/log/dengage.loyalty.frontend}" #set default value
export LOG_DIRECTORY="${LOG_DIRECTORY%/}" #trim the trailing slash

export OUTPUT_DIRECTORY="${OUTPUT_DIRECTORY:-$LOG_DIRECTORY}" #set default value
export OUTPUT_DIRECTORY="${OUTPUT_DIRECTORY%/}" #trim the trailing slash

echo "LISTEN_PORT = ${LISTEN_PORT}"
echo "BACKEND_URL = ${BACKEND_URL}"
echo "WORKING_DIRECTORY = ${WORKING_DIRECTORY}"
echo "LOG_DIRECTORY = ${LOG_DIRECTORY}"
echo "OUTPUT_DIRECTORY = ${OUTPUT_DIRECTORY}"

id

mkdir -p "$WORKING_DIRECTORY"
mkdir -p "$LOG_DIRECTORY"

if [ ! -d "$OUTPUT_DIRECTORY" ]
then
    echo "OUTPUT_DIRECTORY does not exists."
    exit 9999 # die with error code 9999
fi

# Only the listed host names are served; any other host gets 444 (except /ping).
if [ -n "${HOST_NAME_LIST}" ]; then
  export EXTRA_SERVER_BLOCK="
server {
      listen ${LISTEN_PORT} default_server;
      server_name _;
      access_log  off;

      location = /ping {
          proxy_pass ${BACKEND_URL}/health;
          proxy_set_header X-Request-ID \$request_id;
      }

      location / {
          access_log ${LOG_DIRECTORY}/rejected_access.log rejected;
          return 444;
      }
}
"
else
  export EXTRA_SERVER_BLOCK=""
  export HOST_NAME_LIST="_" #set default value
fi

echo "HOST_NAME_LIST = ${HOST_NAME_LIST}"

# prepare nginx.conf file from environment variables
envsubst '${LISTEN_PORT} ${BACKEND_URL} ${WORKING_DIRECTORY} ${LOG_DIRECTORY} ${OUTPUT_DIRECTORY} ${HOST_NAME_LIST} ${EXTRA_SERVER_BLOCK}' < /app/conf/nginx.conf.tpl > /app/conf/nginx.conf

# The environment's portal config (one image for every environment).
if [ -f "/dengage/config/loyalty-frontend.app-config.json" ]; then
    echo "app-config.json replaced by /dengage/config/loyalty-frontend.app-config.json"
    cp /dengage/config/loyalty-frontend.app-config.json /app/www/app-config.json
fi

# If there is a startup script run it first
if [ -f "/dengage/startup-scripts/loyalty-frontend.sh" ]; then #if file exists
    echo "/dengage/startup-scripts/loyalty-frontend.sh will be executed"
    sh /dengage/startup-scripts/loyalty-frontend.sh
fi



PID_FILE="/app/nginx.pid"

cleanup ()
{
    if [ -f "$PID_FILE" ]; then #if pid file exists
        #kill nginx gracefully
        #in order to wait for the pid, we are not killing immediately
        (sleep 1; kill -QUIT $(cat "$PID_FILE")) &
        wait $(cat "$PID_FILE")
    fi
    sleep 1
    echo "exiting"
    exit 0
}

trap cleanup INT TERM QUIT HUP

is_nginx_dead()
{
    if [ -f "$PID_FILE" ]; then #if pid file exists
        if ps -p $(cat "$PID_FILE") > /dev/null; then #if nginx process exists
            return 1 #alive
        fi
    fi
    echo "nginx died"
    return 0
}

#######################################################################
###################### Starting Nginx #################################
#######################################################################
/usr/local/openresty/bin/openresty -p /app -g "daemon off;" &


# nginx check and log rotation
# check nginx every 5 seconds
# do log rotation every 30 seconds
while [ 1 ]
do
    # waiting 30 seconds and checking nginx every 5 seconds
    for i in 1 2 3 4 5 6
    do
        sleep 5 & # sleep in background
        wait $! # wait for sleep process
        if is_nginx_dead
        then
            break 2 #if nginx dead break all loops
        fi
    done
    # rotation log files
    (sh /app/rotate_logs.sh; kill -s USR1 $(cat "$PID_FILE")) &
done

cleanup
