#!/bin/sh
# FluxerTools - first-time setup (macOS, Linux)
cd "$(dirname "$0")"

if [ ! -d ".venv" ]; then
    echo "Creating virtual environment..."
    python3 -m venv .venv
    [ $? -ne 0 ] && { echo "ERROR: Could not create venv. Install Python 3.10+."; exit 1; }
fi

echo "Installing dependencies..."
.venv/bin/pip install -r requirements.txt
[ $? -ne 0 ] && { echo "ERROR: pip install failed."; exit 1; }

echo ""
echo "Setup complete. Run: ./run.sh"
