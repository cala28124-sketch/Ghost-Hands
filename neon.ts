import { defineConfig } from "@neon/config/v1";

export default defineConfig({
  buckets: {
    videos: {}, // private: expert demonstration videos, accessed through signed links from web/server.js
  },
});
