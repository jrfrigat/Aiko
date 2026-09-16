## Description

<!-- What changes, and why. Link the issue or card it belongs to. -->

## Type of change

- [ ] Bug fix
- [ ] New feature
- [ ] Refactoring / improvement
- [ ] Documentation
- [ ] Build, CI or release tooling

## Checklist

- [ ] `dotnet build Aiko.slnx -c Release` passes with no warnings (the build treats warnings as errors)
- [ ] `dotnet test Aiko.slnx -c Release --no-build` passes
- [ ] New behavior has a spec in the matching suite
- [ ] Documentation is updated in both languages (`README.md` + `README.ru.md`, `docs/en` + `docs/ru`)
- [ ] Numbers quoted in documentation (tests, tools, port, paths) still match the code
- [ ] If the release layout changed: `scripts/install.ps1`, `install.ps1` and
      `.github/workflows/release.yml` agree on it
