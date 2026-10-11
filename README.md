# Cato

## A static file web server with live-reload (for development)

<p align="center">
<img width="256" src="Cato-logo.svg" />
</p>

[![Build](https://github.com/mikenakis/Cato/actions/workflows/continuous-integration.yml/badge.svg  )](https://github.com/mikenakis/Cato/actions/workflows/continuous-integration.yml  )

### What does it do?

- It serves via http the contents of a certain directory.

- It implements live-reload.
 
### What is Live Reload?

Live reload means that a page shown in a browser tab gets automatically reloaded if it is changed on the file-system.

### How to use?

- Begin with `dotnet tool install --global cato`.

- Run Cato in a directory to have that directory served at `http://localhost:8080`

- Run Cato with `--help` to see available options.

### How does Live Reload actually work?

Cato embeds a little javascript in every html page that it serves. 
This script opens up a websocket with Cato, and when it receives a message through that websocket, it reloads the page.
Cato listens for file system-changes in the directory containing the files that are being served, and when it
detects a change, it sends a message to each connected websocket, which causes each page to reload.

-----

Named "Cato" after [Cato Fong](https://en.wikipedia.org/wiki/List_of_The_Pink_Panther_characters#Cato_Fong), the trusted 
sidekick of inspector Clouseau in the first line of the Pink Panther movies.

-----

Cover image: The Cato logo, by michael.gr, 
based on ["Fist" by Maxim Kulikov from the Noun Project](https://thenounproject.com/icon/fist-1316322/) ([CC BY 3.0](https://creativecommons.org/licenses/by/3.0/deed.en)) 
and ["Phone" by Lesha Petrick from the Noun Project](https://thenounproject.com/icon/phone-1397798/) ([CC BY 3.0](https://creativecommons.org/licenses/by/3.0/deed.en))
