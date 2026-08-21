FROM node:20-slim

WORKDIR /app

# Install System Dependencies (Python 3, FFmpeg, Curl, Ca-certificates, Unzip)
RUN apt-get update && apt-get install -y --no-install-recommends \
    python3 \
    ffmpeg \
    curl \
    ca-certificates \
    unzip \
    && rm -rf /var/lib/apt/lists/*

# Install Deno JS Runtime (Digunakan oleh --js-runtimes deno pada yt-dlp)
RUN curl -fsSL https://deno.land/install.sh | sh \
    && mv /root/.deno/bin/deno /usr/local/bin/deno \
    && chmod a+rx /usr/local/bin/deno

# Download biner yt-dlp terbaru, beri izin eksekusi, dan perbarui
RUN curl -L https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp -o /usr/local/bin/yt-dlp \
    && chmod a+rx /usr/local/bin/yt-dlp \
    && yt-dlp -U

# Install Dependencies Node.js
COPY package*.json ./
RUN npm ci --only=production

# Copy Kode Sumber Application
COPY server.js ./

# Copy File Cookies (Gunakan wildcard * agar build tidak error jika file cookies.txt tidak ada)
COPY cookies.txt* ./
RUN if [ -f cookies.txt ]; then chmod 644 cookies.txt; fi

EXPOSE 5000

CMD ["npm", "start"]
