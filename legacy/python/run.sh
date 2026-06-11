#!/bin/sh
# FluxerTools - run the bot (macOS, Linux)
cd "$(dirname "$0")"

if [ ! -f ".venv/bin/python" ]; then
    echo "Run setup.sh first to create venv and install dependencies."
    exit 1
fi

if [ ! -f "config.env" ]; then
    echo "Missing config.env! Copy config.env.example to config.env and fill in your values."
    exit 1
fi

exec .venv/bin/python main.py
