import type { NextConfig } from "next";

const nextConfig: NextConfig = {
  // The web app ships as static files (Azure Static Web Apps) and talks to the
  // API cross-origin, so there is no Node server to run in production.
  output: "export",
  images: { unoptimized: true },
  reactStrictMode: true,
};

export default nextConfig;
