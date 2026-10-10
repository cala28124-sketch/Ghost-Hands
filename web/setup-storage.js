// setup-storage.js: one-time setup for the Neon "videos" bucket (declared in the repo-root neon.ts).
// Lets the webpage upload video files straight to the bucket from the browser (CORS),
// then checks the bucket works with a tiny test file.
// Run from web/ after `neon deploy`: node setup-storage.js   (safe to re-run)

const path = require('path');
require('dotenv').config({ path: path.join(__dirname, '..', '.env.local'), quiet: true });

const {
  S3Client, PutBucketCorsCommand, GetBucketCorsCommand,
  PutObjectCommand, GetObjectCommand, DeleteObjectCommand,
} = require('@aws-sdk/client-s3');

const BUCKET = 'videos';
const s3 = new S3Client({ forcePathStyle: true }); // keys, endpoint, and region come from AWS_* in .env.local

async function main() {
  // Signed links already limit who can upload or download, so any page origin may use them.
  await s3.send(new PutBucketCorsCommand({
    Bucket: BUCKET,
    CORSConfiguration: {
      CORSRules: [{
        AllowedOrigins: ['*'],
        AllowedMethods: ['GET', 'PUT', 'HEAD'],
        AllowedHeaders: ['*'],
        ExposeHeaders: ['ETag'],
        MaxAgeSeconds: 3600,
      }],
    },
  }));
  const cors = await s3.send(new GetBucketCorsCommand({ Bucket: BUCKET }));
  console.log(`✓ CORS on "${BUCKET}":`, JSON.stringify(cors.CORSRules?.[0]?.AllowedMethods));

  const key = `setup-check/${Date.now()}.txt`;
  await s3.send(new PutObjectCommand({ Bucket: BUCKET, Key: key, Body: 'ghost hands storage check', ContentType: 'text/plain' }));
  const got = await s3.send(new GetObjectCommand({ Bucket: BUCKET, Key: key }));
  const text = await got.Body.transformToString();
  await s3.send(new DeleteObjectCommand({ Bucket: BUCKET, Key: key }));
  console.log(`✓ Upload, download, delete work (read back: "${text}")`);
}

main().catch((err) => {
  console.error('Storage setup failed:', err.name, err.message);
  console.error('Check that `neon deploy` ran and .env.local has the AWS_* keys.');
  process.exitCode = 1;
});
