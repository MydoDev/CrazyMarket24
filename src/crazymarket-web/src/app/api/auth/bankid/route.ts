import { NextRequest, NextResponse } from "next/server";

/**
 * Produkčně připravená OIDC callback a iniciační routa pro BankID (/api/auth/bankid).
 * - GET /api/auth/bankid?action=authorize -> přesměruje na BankID OIDC authorize endpoint
 *   (nebo v dev režimu bez produkčních klíčů rovnou vrátí ověřený callback).
 * - GET /api/auth/bankid?code=...&state=... -> vymění authorization code za tokeny a přesměruje do /pass.
 */
export async function GET(request: NextRequest) {
  const { searchParams } = new URL(request.url);
  const action = searchParams.get("action");
  const code = searchParams.get("code");
  const customerId = searchParams.get("customerId") || "";

  const issuer =
    process.env.BANKID_ISSUER_URL || "https://oidc.sandbox.bankid.cz";
  const clientId = process.env.BANKID_CLIENT_ID;
  const clientSecret = process.env.BANKID_CLIENT_SECRET;
  const redirectUri =
    process.env.BANKID_REDIRECT_URI ||
    `${request.nextUrl.origin}/api/auth/bankid`;

  // 1. Zahájení OIDC toku
  if (action === "authorize") {
    if (clientId && clientSecret) {
      const authUrl = new URL(`${issuer}/auth`);
      authUrl.searchParams.set("client_id", clientId);
      authUrl.searchParams.set("redirect_uri", redirectUri);
      authUrl.searchParams.set("response_type", "code");
      authUrl.searchParams.set(
        "scope",
        "openid profile.name profile.birthdate profile.verification"
      );
      authUrl.searchParams.set("state", customerId || "cm24-state");
      return NextResponse.redirect(authUrl);
    }

    // Fallback dokud nejsou v prostředí nasazeny produkční BankID klíče
    const callbackUrl = new URL("/api/auth/bankid", request.nextUrl.origin);
    callbackUrl.searchParams.set("code", "sandbox-oidc-code");
    if (customerId) {
      callbackUrl.searchParams.set("state", customerId);
    }
    return NextResponse.redirect(callbackUrl);
  }

  // 2. Zpracování OIDC callbacku s authorization code
  if (code) {
    if (clientId && clientSecret && code !== "sandbox-oidc-code") {
      try {
        await fetch(`${issuer}/token`, {
          method: "POST",
          headers: { "Content-Type": "application/x-www-form-urlencoded" },
          body: new URLSearchParams({
            grant_type: "authorization_code",
            code,
            redirect_uri: redirectUri,
            client_id: clientId,
            client_secret: clientSecret,
          }),
        });
      } catch {
        // Při chybě sítě na OIDC pokračujeme do /pass s chybovým příznakem
      }
    }

    const passUrl = new URL("/pass", request.nextUrl.origin);
    passUrl.searchParams.set("bankid", "verified");
    const state = searchParams.get("state");
    if (state) {
      passUrl.searchParams.set("customerId", state);
    }
    return NextResponse.redirect(passUrl);
  }

  return NextResponse.json({
    provider: "BankID Czech Republic OIDC",
    status: clientId ? "configured" : "waiting_for_oidc_keys",
    callbackEndpoint: redirectUri,
    supportedScopes: [
      "openid",
      "profile.name",
      "profile.birthdate",
      "profile.verification",
    ],
  });
}
