# FluxerTools - run on NAS or any Docker host
FROM python:3.11-slim

# ffmpeg required for TTS voice playback
RUN apt-get update && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY . .

# config.env must be copied or mounted at runtime with secrets
CMD ["python", "main.py"]
