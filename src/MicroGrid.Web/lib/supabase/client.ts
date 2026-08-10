"use client";

import { createBrowserClient } from "@supabase/ssr";
import { publicSupabaseEnv } from "@/lib/env";

export function createClient() {
  const env = publicSupabaseEnv();
  return createBrowserClient(env.url, env.publishableKey);
}
