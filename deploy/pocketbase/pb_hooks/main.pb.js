const EVE_TOKEN_URL = "https://login.eveonline.com/v2/oauth/token";

function asString(value) {
  return value === undefined || value === null ? "" : String(value);
}

routerAdd("POST", "/api/eve-industry/auth/eve/callback", (e) => {
  try {
    const body = e.requestInfo().body || {};
    const code = asString(body.code);
    const codeVerifier = asString(body.code_verifier);
    const redirectUri = asString(body.redirect_uri) || $os.getenv("EVE_SSO_REDIRECT_URI");
    const clientId = $os.getenv("EVE_SSO_CLIENT_ID");
    const clientSecret = $os.getenv("EVE_SSO_CLIENT_SECRET");
    const tokenEncryptionKey = $os.getenv("TOKEN_ENCRYPTION_KEY");

    if (!code) {
      return e.json(400, { error: "missing_code", message: "Missing EVE SSO authorization code." });
    }

    if (!redirectUri) {
      return e.json(400, { error: "missing_redirect_uri", message: "Missing EVE SSO redirect URI." });
    }

    if (!clientId) {
      return e.json(400, { error: "missing_client_id", message: "Server is missing EVE_SSO_CLIENT_ID." });
    }

    if (!tokenEncryptionKey || tokenEncryptionKey.length !== 32) {
      return e.json(400, { error: "invalid_token_key", message: "Server TOKEN_ENCRYPTION_KEY must be exactly 32 characters." });
    }

    const eveToken = exchangeEveAuthorizationCode(code, codeVerifier, redirectUri, clientId, clientSecret);
    const identity = readEveIdentity(eveToken.access_token);
    const authUser = resolvePocketBaseUser(e, identity);
    const eveAccount = upsertEveAccount(authUser, identity, eveToken, tokenEncryptionKey);

    return e.json(200, {
      token: authUser.newAuthToken(),
      record: authUser.publicExport(),
      eve_account: eveAccount.publicExport(),
    });
  } catch (err) {
    return e.json(400, {
      error: "eve_auth_failed",
      message: String(err && err.message ? err.message : err),
    });
  }
});

function exchangeEveAuthorizationCode(code, codeVerifier, redirectUri, clientId, clientSecret) {
  const form = {
    grant_type: "authorization_code",
    code: code,
    redirect_uri: redirectUri,
    client_id: clientId,
  };

  if (codeVerifier) {
    form.code_verifier = codeVerifier;
  }

  if (!codeVerifier && clientSecret) {
    form.client_secret = clientSecret;
  }

  const response = $http.send({
    url: EVE_TOKEN_URL,
    method: "POST",
    body: encodeForm(form),
    headers: {
      "content-type": "application/x-www-form-urlencoded",
    },
    timeout: 30,
  });

  if (response.statusCode < 200 || response.statusCode >= 300) {
    throw new BadRequestError("EVE SSO token exchange failed: " + String(response.raw || response.body || response.statusCode));
  }

  if (!response.json || !response.json.access_token) {
    throw new BadRequestError("EVE SSO token response did not contain an access token.");
  }

  return response.json;
}

function readEveIdentity(accessToken) {
  const claims = $security.parseUnverifiedJWT(accessToken);
  const sub = asString(claims.sub);
  const characterId = sub.split(":").pop();
  const characterName = asString(claims.name);

  if (!characterId || !characterName) {
    throw new BadRequestError("EVE SSO token did not contain a character identity.");
  }

  return {
    character_id: characterId,
    character_name: characterName,
    scopes: normalizeScopes(claims.scp),
  };
}

function resolvePocketBaseUser(e, identity) {
  const requestAuth = e.requestInfo().auth;
  if (requestAuth && requestAuth.collection().name === "users") {
    return requestAuth;
  }

  try {
    const existingAccount = $app.findFirstRecordByData("eve_accounts", "character_id", identity.character_id);
    return $app.findRecordById("users", existingAccount.getString("user"));
  } catch (_) {
  }

  const users = $app.findCollectionByNameOrId("users");
  const user = new Record(users);
  user.setEmail("eve-" + identity.character_id + "@eve.local");
  user.setVerified(true);
  user.setRandomPassword();
  $app.save(user);
  return user;
}

function upsertEveAccount(user, identity, token, tokenEncryptionKey) {
  let account = null;
  try {
    account = $app.findFirstRecordByData("eve_accounts", "character_id", identity.character_id);
  } catch (err) {
    const collection = $app.findCollectionByNameOrId("eve_accounts");
    account = new Record(collection);
    account.set("character_id", identity.character_id);
  }

  const ownerId = account.getString("user");
  if (ownerId && ownerId !== user.id) {
    throw new BadRequestError("This EVE character is already linked to another user.");
  }

  const expiresIn = Number(token.expires_in || 1200);
  const expiresAt = new Date(Date.now() + expiresIn * 1000).toISOString();
  const tokenBundle = {
    access_token: asString(token.access_token),
    refresh_token: asString(token.refresh_token),
    token_type: asString(token.token_type) || "Bearer",
    expires_at: expiresAt,
  };

  account.set("user", user.id);
  account.set("character_name", identity.character_name);
  account.set("corporation_id", "");
  account.set("scopes", identity.scopes);
  account.set("token_ciphertext", $security.encrypt(JSON.stringify(tokenBundle), tokenEncryptionKey));
  account.set("token_expires_at", expiresAt);
  $app.save(account);

  return account;
}

function normalizeScopes(scopes) {
  if (!scopes) {
    return [];
  }

  if (Array.isArray(scopes)) {
    return scopes.map(asString).filter((scope) => scope.length > 0);
  }

  return String(scopes)
    .split(" ")
    .map((scope) => scope.trim())
    .filter((scope) => scope.length > 0);
}

function encodeForm(values) {
  return Object.keys(values)
    .filter((key) => values[key] !== undefined && values[key] !== null && String(values[key]).length > 0)
    .map((key) => encodeURIComponent(key) + "=" + encodeURIComponent(String(values[key])))
    .join("&");
}
