import type { Metadata } from "next";
import "./globals.css";

export const metadata: Metadata = {
  title: "BallBank",
  description:
    "The books for your fantasy league's dues: who owes, who paid, who confirmed, who got paid out.",
};

export default function RootLayout({ children }: LayoutProps<"/">) {
  return (
    <html lang="en" className="h-full antialiased">
      <body className="min-h-full flex flex-col">{children}</body>
    </html>
  );
}
