FROM node:22-alpine AS build

WORKDIR /app

COPY package*.json ./

RUN npm install

COPY . .

RUN npm run build

FROM node:22-alpine

WORKDIR /app

RUN npm install -g http-server

COPY --from=build /app/dist/wallet-ui/browser ./dist

EXPOSE 4200

CMD ["http-server", "dist", "-p", "4200", "-a", "0.0.0.0"]