#!/bin/bash

# Get the current directory
REPO_DIR=$(pwd)
echo "$REPO_DIR"

# Run the Docker container with the specified environment variables and volume mount
docker run \
	-e FILTER_REGEX_EXCLUDE='.*wwwroot/lib/.*' \
	-e IGNORE_GITIGNORED_FILES=true \
	-e LOG_LEVEL=INFO \
	-e DEFAULT_BRANCH=origin/develop \
	-e RUN_LOCAL=true \
	-e VALIDATE_ALL_CODEBASE=true \
	-v "$REPO_DIR:/tmp/lint" -it --rm ghcr.io/super-linter/super-linter:v7.1.0
