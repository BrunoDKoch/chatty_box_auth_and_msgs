# What is ChattyBox?

ChattyBox is an open-source real-time chat app, developed mostly as a demonstration piece, but which is production ready.

This repository, specifically, is the back-end of the app.

## What are the core features of ChattyBox?

> Real-time communication between users, using Microsoft's SignalR;
> Multi-language support (English, Portuguese and Spanish), thanks to the use of Microsoft's Localization extension on the back-end, and Svelte's i18n package on the front-end;
> Secure authentication via ASP.NET Core's Identity Module, with the possibility of integrating third-party logins in the future;
> Prevention of logins from suspicious locations;
> Multi-factor authentication;
> File sharing between users, with cached previews for images.

## Why the weird architecture? You know SvelteKit is a full-stack framework, right?

Yep. I also happen to know that ASP.NET Core has official first-party packages for real-time communications, caching, authentication and even an ORM in the form of EntityFramework Core.

While I certainly *could've* built this thing entirely with SvelteKit, prior experiences have made me wary of authentication frameworks for JavaScript. Therefore, I opted to use a combination of ASP.NET Core and SvelteKit.

## Self-hosting

### Requirements
You will need .NET version 7 or greater, and Node.js version 18 or greater.

It is recommended that you use Docker, pulling the official [SQL Server][https://hub.docker.com/_/microsoft-mssql-server] and [Redis][https://hub.docker.com/_/redis] images, although you're free to run them directly on your own machine, or in a remote server.

In order to use the suspicious location prevention feature, you will need a [MaxMind GeoLite](https://www.maxmind.com) API key.

Finally, you'll need [Sentry][https://sentry.io/welcome/] for error logging, which can be self-hosted alongside ChattyBox.
