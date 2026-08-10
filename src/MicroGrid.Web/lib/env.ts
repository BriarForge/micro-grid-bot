const placeholder = /REPLACE_ME|YOUR_PROJECT_REF/;

export function hasSupabaseEnv() {
  const url = process.env.NEXT_PUBLIC_SUPABASE_URL;
  const key = process.env.NEXT_PUBLIC_SUPABASE_PUBLISHABLE_KEY;
  return Boolean(url && key && !placeholder.test(url) && !placeholder.test(key));
}

export function publicSupabaseEnv() {
  if (!hasSupabaseEnv()) {
    throw new Error("Supabase is not configured. Copy .env.example to .env.local and set the public URL and publishable key.");
  }
  return {
    url: process.env.NEXT_PUBLIC_SUPABASE_URL!,
    publishableKey: process.env.NEXT_PUBLIC_SUPABASE_PUBLISHABLE_KEY!,
  };
}
