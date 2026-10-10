// storage.js: the server's connection to the Neon "videos" bucket (Neon Object Storage, S3-compatible).
// Makes one-time signed links so the webpage can upload and Dominick's script can download,
// without either of them ever holding storage keys. Keys stay in the repo-root .env.local.
// Usage: const storage = require('./storage');
//   storage.makeVideoKey('cube.mp4')            → 'uploads/<random>-cube.mp4'
//   await storage.uploadUrl(key, 'video/mp4')   → https://... upload link (valid 15 min)
//   await storage.downloadUrl(key)              → https://... download link (valid 15 min)
//   await storage.fileExists(key)               → { exists, size }

const path = require('path');
const { randomUUID } = require('crypto');
require('dotenv').config({ path: path.join(__dirname, '..', '.env.local'), quiet: true });

const { S3Client, PutObjectCommand, GetObjectCommand, HeadObjectCommand } = require('@aws-sdk/client-s3');
const { getSignedUrl } = require('@aws-sdk/s3-request-presigner');

const BUCKET = 'videos';
const LINK_SECONDS = 15 * 60;
const s3 = new S3Client({ forcePathStyle: true }); // keys, endpoint, and region come from AWS_* in .env.local

// Unique, safe name inside the bucket: uploads/<uuid>-<cleaned file name>.
// Stored in the videos table's storage_key column.
function makeVideoKey(fileName) {
  const safe = String(fileName).toLowerCase().replace(/[^a-z0-9.-]+/g, '-').slice(-80) || 'video';
  return `uploads/${randomUUID()}-${safe}`;
}

// One-time link the browser uses to PUT one file to this key.
// Content-Type is part of the signature, so the upload must send the same type or Neon rejects it (403).
function uploadUrl(key, contentType) {
  return getSignedUrl(s3, new PutObjectCommand({ Bucket: BUCKET, Key: key, ContentType: contentType }), {
    expiresIn: LINK_SECONDS,
    signableHeaders: new Set(['content-type']),
  });
}

// One-time link Dominick's script uses to GET one file.
function downloadUrl(key) {
  return getSignedUrl(s3, new GetObjectCommand({ Bucket: BUCKET, Key: key }), { expiresIn: LINK_SECONDS });
}

// Checks whether a file arrived in the bucket, without downloading it.
// Missing → { exists: false }; any other problem (keys, network) is thrown.
async function fileExists(key) {
  try {
    const head = await s3.send(new HeadObjectCommand({ Bucket: BUCKET, Key: key }));
    return { exists: true, size: head.ContentLength };
  } catch (err) {
    if (err.name === 'NotFound' || err.$metadata?.httpStatusCode === 404) return { exists: false, size: 0 };
    throw err;
  }
}

module.exports = { makeVideoKey, uploadUrl, downloadUrl, fileExists };
