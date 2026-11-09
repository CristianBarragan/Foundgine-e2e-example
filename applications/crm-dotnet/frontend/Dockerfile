FROM node:20-alpine AS build
WORKDIR /app
ARG VITE_KEYCLOAK_URL=http://localhost:8080
ARG VITE_API_BASE=
ENV VITE_KEYCLOAK_URL=$VITE_KEYCLOAK_URL VITE_API_BASE=$VITE_API_BASE
COPY package*.json ./
RUN npm install
COPY . .
RUN npm run build
FROM nginx:1.27-alpine
COPY --from=build /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
