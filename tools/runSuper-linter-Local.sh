#!/bin/bash

# Get the current directory
REPO_DIR=$(pwd)
echo "$REPO_DIR"

docker pull ghcr.io/super-linter/super-linter:v8.5.0

# Run the Docker container with the specified environment variables and volume mount
docker run \
	-e FILTER_REGEX_EXCLUDE='.*wwwroot/.*' \
	-e LOG_LEVEL=INFO \
	-e DEFAULT_BRANCH=origin/develop \
	-e RUN_LOCAL=true \
	-e VALIDATE_ALL_CODEBASE=true \
	-e VALIDATE_BIOME_LINT=false \
	-e VALIDATE_BIOME_FORMAT=false \
	-e VALIDATE_TRIVY=false \
	-e VALIDATE_CHECKOV=false \
	-e VALIDATE_GIT_COMMITLINT=false \
	-e VALIDATE_GITHUB_ACTIONS_ZIZMOR=false \
	-e ENABLE_GITHUB_PULL_REQUEST_SUMMARY_COMMENT=false \
	-e VALIDATE_TRIVY=false \
	-e VALIDATE_PYTHON_BLACK=false \
	-v "$REPO_DIR:/tmp/lint" -it --rm ghcr.io/super-linter/super-linter:v8.5.0
