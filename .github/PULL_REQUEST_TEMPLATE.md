## What this changes

<!-- The behaviour that is different after this PR, from a host's point of view. -->

## Why

<!-- The problem it solves. Link the issue: Fixes #123 -->

## How it was verified

<!--
Which of these you ran, and what happened. Say plainly if something was skipped.
  dotnet build KHost.slnx "-p:BaseOutputPath=./obj/_build"
  dotnet test tests/KHost.UnitTests
  dotnet test tests/KHost.IntegrationTests     # needs ffmpeg/ffprobe
  dotnet run --project src/KHost.UserInterface # clicked through: ...
-->

## Screenshots

<!-- UI changes only. Before and after where it helps. -->

## Checklist

- [ ] Unit tests pass, and they are skip-free
- [ ] New or edited tests were mutation-swept — say which branches, and which were left
- [ ] Schema change (any `DbSet<T>` model)? A migration is included
- [ ] New interfaces live in `KHost.Abstractions`, implementations in `Domain`/`DataAccess`, registered in the project's `ProjectExtensions`
- [ ] Component logic is in a `.razor.cs` partial; no inline `@code` blocks
- [ ] Styles are BEM `kh-` SCSS beside the component or under `wwwroot/scss` — no inline styles, no generated `.razor.css` committed
- [ ] Every `Task`/`ValueTask` method ends in `Async`
- [ ] A public contract change (`KHost.Plugins.Sdk`, IPC commands) is called out below

## Notes for reviewers

<!-- Anything not obvious from the diff: a trade-off taken, a follow-up left, a risk. -->
