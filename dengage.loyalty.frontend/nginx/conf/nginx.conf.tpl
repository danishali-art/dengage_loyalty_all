error_log  ${LOG_DIRECTORY}/error.log;
pid        /app/nginx.pid;

worker_processes  1;
events {
    worker_connections 10000;
}

http {
    include mime.types;

    client_body_temp_path /tmp/client_temp;
    proxy_temp_path       /tmp/proxy_temp_path;
    fastcgi_temp_path     /tmp/fastcgi_temp;
    uwsgi_temp_path       /tmp/uwsgi_temp;
    scgi_temp_path        /tmp/scgi_temp;

    # As the panel's "apm" format, without any auth header: the portal sends a bearer token and
    # API keys, which must never reach a log file.
    log_format apm '"$time_iso8601"  "$request"   '
               'request_length=$request_length '
               'status=$status bytes_sent=$bytes_sent '
               'request_time=$request_time '
               'upstream_response_time=$upstream_response_time '
               'upstream_connect_time=$upstream_connect_time '
               'user_agent="$http_user_agent" '
               'request_id="$request_id" '
               'ip="$http_x_dengage_ipaddr" '
               'referer="$http_referer" ';

    log_format rejected '"$time_iso8601" host="$host" ip="$remote_addr" request="$request" user_agent="$http_user_agent"';

  client_max_body_size 10m;

  ${EXTRA_SERVER_BLOCK}

  server {
    listen	 ${LISTEN_PORT};
    server_name ${HOST_NAME_LIST};

    sendfile     on;
    access_log  off;

    add_header X-Request-ID $request_id; # Return to client
    add_header X-Content-Type-Options "nosniff";
    add_header Referrer-Policy "same-origin";
    # The portal loads nothing from other origins and calls only its own /api. Inline styles are
    # allowed because the UI components set positioning styles at runtime; scripts are not.
    add_header Content-Security-Policy "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; connect-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none';" always;
    add_header Strict-Transport-Security "max-age=31536000; includeSubDomains" always;

    root /app/www;
    index index.html;

    # Vite's content-hashed build output: the file name changes with the content, so it can be
    # cached forever.
    location ^~ /assets/ {
        # more_set_headers: unlike add_header it keeps the server-level headers (CSP/HSTS).
        more_set_headers -s '200 206 304' "Cache-Control: public, max-age=31536000, immutable";
        try_files $uri =404;
    }

    # Entry point and runtime config are never cached, so a deploy or a config change is seen at once.
    location = / {
        more_set_headers "Cache-Control: no-cache, no-store, must-revalidate";
        try_files /index.html =404;
    }
    location = /index.html {
        more_set_headers "Cache-Control: no-cache, no-store, must-revalidate";
        try_files $uri =404;
    }
    # The portal fetches app-config.json relative to the current page, so a reload on a deep link
    # (/customers/abc) asks for /customers/app-config.json: every such path gets the one file.
    location ~ (^|/)app-config\.json$ {
        more_set_headers "Cache-Control: no-cache, no-store, must-revalidate";
        try_files /app-config.json =404;
    }

    # Client-side routes (vue-router history mode) fall back to index.html.
    location / {
        try_files $uri /index.html;
    }

    location /api/ {
      access_log   ${LOG_DIRECTORY}/api.log  apm;
      proxy_pass ${BACKEND_URL};
      proxy_set_header Host $host;
      proxy_set_header X-Request-ID $request_id;
      proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
      proxy_set_header X-Forwarded-Proto $scheme;
      proxy_read_timeout 300s;
    }

    # Liveness through to the Api, as the panel's /ping.
    location = /ping {
      proxy_pass ${BACKEND_URL}/health;
      proxy_set_header X-Request-ID $request_id;
    }

    error_page 500 502 503 504 /50x.html;
    location = /50x.html {
        root /app/html;
        internal;
    }

    location = /robots.txt {
        return 200 'User-agent: *\nDisallow: /';
    }

    include /dengage/config/loyalty-frontend.server-extra[.]conf;

  }

  include /dengage/config/loyalty-frontend.http-extra[.]conf;

}

include /dengage/config/loyalty-frontend.global-extra[.]conf;
