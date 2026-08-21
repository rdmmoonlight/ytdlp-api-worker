FROM node:20-slim

WORKDIR /app

# Install Python, FFmpeg, Curl, & Ca-certificates
RUN apt-get update && apt-get install -y --no-install-recommends \
    python3 \
    ffmpeg \
    curl \
    ca-certificates \
    unzip \
    && rm -rf /var/lib/apt/lists/*

# Install Deno JS Runtime (Engine pendukung bypass bot YouTube)
RUN curl -fsSL https://deno.land/install.sh | sh \
    && mv /root/.deno/bin/deno /usr/local/bin/deno \
    && chmod a+rx /usr/local/bin/deno

# Install Executable yt-dlp
RUN curl -L https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp -o /usr/local/bin/yt-dlp \
    && chmod a+rx /usr/local/bin/yt-dlp \
    && yt-dlp -U

# Install Node Dependencies
COPY package.json ./
RUN npm install --production

# Copy Source Code Server
COPY server.js ./

# Copy File Cookies (jika ada)
COPY cookies.txt* /app/
RUN if [ -f /app/cookies.txt ]; then chmod 644 /app/cookies.txt; fi

EXPOSE 5000

CMD ["npm", "start"]
