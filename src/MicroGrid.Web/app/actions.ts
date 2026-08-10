"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { createClient } from "@/lib/supabase/server";

export async function signIn(formData: FormData) {
  const email = String(formData.get("email") ?? "").trim();
  if (!email) redirect("/?error=email-required");
  const supabase = await createClient();
  const siteUrl = process.env.NEXT_PUBLIC_SITE_URL ?? "http://localhost:3000";
  const { error } = await supabase.auth.signInWithOtp({ email, options: { emailRedirectTo: `${siteUrl}/auth/callback` } });
  if (error) redirect(`/?error=${encodeURIComponent(error.message)}`);
  redirect("/?sent=1");
}

export async function signOut() {
  const supabase = await createClient();
  await supabase.auth.signOut();
  redirect("/");
}

export async function requestCommand(formData: FormData) {
  const type = String(formData.get("type") ?? "");
  const botId = String(formData.get("botId") ?? "");
  if (!new Set(["pause", "resume", "recenter", "rescale", "emergency_stop"]).has(type)) throw new Error("Unsupported command");
  const supabase = await createClient();
  const { data: claims } = await supabase.auth.getClaims();
  const userId = claims?.claims?.sub;
  if (!userId) redirect("/");
  const { error } = await supabase.from("bot_commands").insert({
    bot_id: botId,
    type,
    requested_by: userId,
    idempotency_key: crypto.randomUUID(),
    expires_at: new Date(Date.now() + 5 * 60_000).toISOString(),
  });
  if (error) throw new Error(error.message);
  revalidatePath("/");
}
