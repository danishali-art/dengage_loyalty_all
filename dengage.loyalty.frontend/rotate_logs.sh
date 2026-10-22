#!/bin/sh

# Same rotation as the dEngage panel frontend (adminpanel/rotate_logs.sh).

PID_FILE="/app/nginx.pid"

cd $LOG_DIRECTORY

manage_other_logs() {
    # $1 = filename
    # $2 = max size
    # $3 = max count
    FILE_NAME="$LOG_DIRECTORY/$1.log"
    if [ -f "$FILE_NAME" ]; then #if file exists
        filesize=$(stat -c%s "$FILE_NAME")
        maxsize=$2
        if [ $filesize -gt $maxsize ]; then #if bigger than max size
            #rotate file with hostname
            mv $FILE_NAME "${OUTPUT_DIRECTORY}/$1-$HOSTNAME-$(date +\%FT\%H-\%M-\%S).log";

            # if there are files more than max count, delete old ones
            ls -t ${OUTPUT_DIRECTORY}/$1-$HOSTNAME-*.log | sed -e "1,$3d" | xargs -r -d '\n' rm
        fi
    fi
}


# rotate log files by max size and count
manage_other_logs "error" 1000111 20
manage_other_logs "api" 10000111 10
manage_other_logs "rejected_access" 1000111 5



# Notify Nginx to reopen the log files
kill -USR1 $(cat "$PID_FILE")
