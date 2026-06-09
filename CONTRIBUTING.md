# Contributing to KHost

Thanks for your interest in improving KHost! Contributions of all kinds are
welcome — bug reports, fixes, features, docs, and tests.

## How to contribute

1. **Open an issue first** for anything non-trivial (new features, refactors,
   dependency changes) so the approach can be agreed before you invest time.
2. Fork, branch from `master`, and keep changes focused.
3. Build and test before opening a pull request:
   ```bash
   dotnet build KHost.slnx
   dotnet test KHost.UnitTests
   ```
4. Match the existing code style and conventions (see `CLAUDE.md` for the
   project's structure, naming, and member-ordering rules).
5. Open a pull request describing **what** changed and **why**.

## License of your contributions

KHost is offered under the [PolyForm Shield License 1.0.0](LICENSE) **and** under
separate commercial licenses sold by the maintainer. For that dual-licensing to
keep working, the maintainer must hold the rights to relicense every line in the
project. Therefore, **by submitting a contribution you agree that:**

- You license your contribution to the project and its users under the same
  **PolyForm Shield License 1.0.0** that covers KHost; **and**
- You grant **Michael Riddle** (the maintainer/licensor) a perpetual, worldwide,
  non-exclusive, royalty-free, irrevocable license to use, reproduce, modify,
  distribute, and **relicense** your contribution under any terms, **including
  proprietary and commercial licenses**; and
- You have the legal right to grant these rights — the contribution is your own
  original work, or you have permission to submit it, and it does not knowingly
  infringe anyone's rights.

This lets the project remain free and source-available for the community while
allowing the maintainer to offer the commercial/SaaS/OEM licenses described in
the [README](README.md#license). You retain copyright in your contribution; you
are simply granting these licenses.

## Sign your commits (DCO)

Add a `Signed-off-by` line to each commit to certify the
[Developer Certificate of Origin](https://developercertificate.org/):

```bash
git commit -s -m "Your message"
```

Once the project starts accepting outside pull requests regularly, a lightweight
Contributor License Agreement (e.g. via [CLA Assistant](https://cla-assistant.io/))
may be required before a PR can be merged. The DCO sign-off documents provenance;
the license grant in the section above is what preserves the project's
dual-licensing rights.

## Commit messages

- Write clear, imperative subjects (e.g. "Add screen reconnect handling").
- **Do not** add `Co-Authored-By` lines or AI/tool attribution footers.
