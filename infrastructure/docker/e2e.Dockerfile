FROM alpine:3.20
RUN apk add --no-cache bash curl jq
WORKDIR /e2e
ENTRYPOINT ["bash", "/e2e/run.sh"]
