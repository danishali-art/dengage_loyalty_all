# The Vue admin portal (webVue3), packaged like the dEngage panel (adminpanel's
# dengage.adminpanel.frontend): a Node build stage, then OpenResty serving the static build and
# forwarding /api to the loyalty Api. The Angular portal (web/) is not part of any image.
#
# Build context: the repo root, as for every dEngage image:
#   docker build -t dengage.loyalty.frontend -f dengage.loyalty.frontend/Dockerfile .
FROM node:24.14.1-bookworm AS build

WORKDIR /repo

# `npm run build` first runs the OpenAPI drift check (api:check), which regenerates the spec from
# the backend C# source in src/, so the build fails when the portal is behind the API.
COPY src ./src
COPY webVue3/package.json webVue3/package-lock.json ./webVue3/
WORKDIR /repo/webVue3
RUN npm ci
COPY webVue3 ./
RUN npm run build



FROM openresty/openresty:alpine-apk AS runtime
ARG user=dengageuser
ARG uid=1566

RUN adduser -u ${uid} -S ${user} -g 0 -s /bin/sh

RUN apk --no-cache add procps gettext coreutils && apk del curl && apk upgrade

# nginx prefix (-p /app): conf/ and html/ (error pages) live here, the site itself in /app/www,
# so the scripts and the config template are never served.
COPY dengage.loyalty.frontend/nginx /app
COPY dengage.loyalty.frontend/start.sh dengage.loyalty.frontend/rotate_logs.sh /app/
# A Windows checkout may have CRLF line endings, which sh can't run.
RUN sed -i 's/\r$//' /app/start.sh /app/rotate_logs.sh

RUN mkdir -p /dengage && chown -R ${user}:root /dengage

WORKDIR /app
COPY --chown=${user}:root --from=build /repo/webVue3/dist ./www

# OpenResty opens its default logs/error.log before it reads nginx.conf.
RUN mkdir -p /tmp/cache/ /app/logs
RUN chown -R ${user}:root /app && chmod -R g+rwX /app
RUN chown -R ${user}:root /tmp && chmod -R g+rwX /tmp

USER dengageuser

# Runtime settings (environment): LISTEN_PORT (8080), BACKEND_URL (the loyalty Api),
# HOST_NAME_LIST, LOG_DIRECTORY, OUTPUT_DIRECTORY, WORKING_DIRECTORY. See start.sh.
EXPOSE 8080

ENTRYPOINT ["sh", "/app/start.sh"]
