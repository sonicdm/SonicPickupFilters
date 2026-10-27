#!/usr/bin/env python3
"""Write thunderstore.toml for tcli publish from env + manifest.json."""

from __future__ import annotations

import json
import os
import pathlib


def main() -> None:
    name = os.environ["PKG_NAME"]
    version = os.environ["PKG_VERSION"]
    description = os.environ["PKG_DESCRIPTION"].replace("\\", "\\\\").replace('"', '\\"')
    website = os.environ.get("PKG_WEBSITE") or f"https://github.com/sonicdm/{name}"
    manifest = json.loads(pathlib.Path("manifest.json").read_text(encoding="utf-8"))

    dep_lines = []
    for dep in manifest.get("dependencies") or []:
        package_id, ver = dep.rsplit("-", 1)
        dep_lines.append(f'{package_id} = "{ver}"')
    deps_block = "\n".join(dep_lines)

    text = f"""[config]
schemaVersion = "0.0.1"

[package]
namespace = "SonicDM"
name = "{name}"
versionNumber = "{version}"
description = "{description}"
websiteUrl = "{website}"
containsNsfwContent = false

[package.dependencies]
{deps_block}

[build]
icon = "./icon.png"
readme = "./README.md"
outdir = "./build"

[[build.copy]]
source = "./dist"
target = ""

[publish]
repository = "thunderstore.io"
communities = ["valheim"]
"""
    pathlib.Path("thunderstore.toml").write_text(text, encoding="utf-8")
    print(text)


if __name__ == "__main__":
    main()
