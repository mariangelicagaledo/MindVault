# MindVault

This repository contains the .NET MAUI app MindVault.

## Getting started with GitHub backup

1. Create a new empty repository on GitHub (no README, license, or .gitignore).
2. Add the remote and push:

```
# replace <URL> with your repo URL, e.g. https://github.com/<user>/mindvault.git
git remote add origin <URL>
# push the current main branch
git branch -M main
git push -u origin main
```

## Tagging versions

```
# create a version tag
git tag -a v0.1.0 -m "First version"
# push tags
git push --tags
```

## Build
- .NET 9 / .NET MAUI
- Windows, Android, iOS, Mac Catalyst targets
