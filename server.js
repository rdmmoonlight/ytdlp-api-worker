const express = require('express');
const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');
const cors = require('cors');

const app = express();
const PORT = process.env.PORT || 5000;
const DOWNLOAD_DIR = process.env.DOWNLOAD_DIR || '/app/downloads';

app.use(cors());
app.use(express.json());

// Pastikan folder download tersedia
if (!fs.existsSync(DOWNLOAD_DIR)) {
    fs.mkdirSync(DOWNLOAD_DIR, { recursive: true });
}

// 1. Health Check Endpoint
app.get('/health', (req, res) => {
    res.json({ status: 'OK', message: 'yt-dlp worker service is running.' });
});

// 2. Stream Audio Download Endpoint
app.post('/api/download/audio', (req, res) => {
    const { url } = req.body;

    if (!url) {
        return res.status(400).json({ error: 'URL tidak boleh kosong.' });
    }

    // Set Header untuk Server-Sent Events (SSE) / Live Text Stream ke C#
    res.setHeader('Content-Type', 'text/plain; charset=utf-8');
    res.setHeader('Transfer-Encoding', 'chunked');

    const cookiePath = '/app/cookies.txt';
    const args = [
        '--no-warnings',
        '--no-cache-dir',
        '--newline',
        '--ignore-config',
        '--force-overwrites',
        '--add-metadata',
        '-x',
        '--audio-format', 'mp3',
        '--audio-quality', '0',
        '-o', path.join(DOWNLOAD_DIR, '%(title)s.%(ext)s'),
        url
    ];

    if (fs.existsSync(cookiePath)) {
        args.unshift('--cookies', cookiePath);
    }

    res.write(`[INIT] Memulai proses ekstraksi yt-dlp untuk: ${url}\n`);

    const ytdlp = spawn('yt-dlp', args);

    ytdlp.stdout.on('data', (data) => {
        res.write(data.toString());
    });

    ytdlp.stderr.on('data', (data) => {
        res.write(`[STDERR] ${data.toString()}`);
    });

    ytdlp.on('close', (code) => {
        if (code === 0) {
            res.write('[COMPLETED] Ekstraksi audio selesai dengan sukses!\n');
        } else {
            res.write(`[ERROR] yt-dlp keluar dengan exit code: ${code}\n`);
        }
        res.end();
    });

    ytdlp.on('error', (err) => {
        res.write(`[FATAL] Gagal mengeksekusi yt-dlp: ${err.message}\n`);
        res.end();
    });
});

app.listen(PORT, () => {
    console.log(`yt-dlp Worker API berjalan di port ${PORT}`);
});
