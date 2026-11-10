# web: kan!'s website

The page at `https://kan.kairophos.link`: what kan! is, which instruments
are tested, downloads and privacy. One static page, no build step.

- `index.html`: the page (fonts from Google Fonts, everything else inline).
- `logo.png`, `favicon.png`, `dusk.jpg`: resized from `game/brand/` (the
  game's provisional art; regenerate them when that art changes).

Search engines: `robots.txt` allows every crawler and points at
`sitemap.xml`, which lists the one page URL. `index.html` carries the
canonical link, robots and theme-color meta, Open Graph and Twitter card
tags, and JSON-LD (the game plus its FAQ), all mirroring facts already on
the page.

Hosting: Cloudflare Pages, connected to this repository, production branch
`main`, no build command, output directory `web`. A push to `main` publishes
it. The download buttons point at
`github.com/KairoPhos13/kan-rhythm/releases/latest/download/kan-<rid>.zip`,
the zips the CI attaches to a release (`.github/workflows/build.yml`), so
they work once a release is published.

The game server is a separate host, `kan-api.kairophos.link`
(`server/app`); a browser that opens it is redirected here (`WEBSITE_URL`).
