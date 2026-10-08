## Development

### CSS/SCSS

SCSS is compiled by `AspNetCore.SassCompiler` as part of `dotnet build` (and `dotnet watch`); there
is no separate sass npm script. Edit the `.scss` files (including co-located `*.razor.scss`) and
rebuild.

### npm

`package.json` has two scripts: `dev` (`dotnet watch`) and `copy:vendors`, which stages vendor
scripts into `wwwroot/js/`. The build runs `copy:vendors` itself; run `npm install` first so
`node_modules` exists.
