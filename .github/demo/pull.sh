#!/bin/bash
set -e
curl -fsSLO "https://raw.githubusercontent.com/melosso/beacon/main/.github/demo/reset-demo.sh"
chmod +x reset-demo.sh
echo "Run './reset-demo.sh' to start the demo."
