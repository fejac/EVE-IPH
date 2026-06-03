routerAdd("POST", "/api/eve-industry/auth/eve/callback", function(e) {
  var EVE_TOKEN_URL = "https://login.eveonline.com/v2/oauth/token";

  function asString(value) {
    return value === undefined || value === null ? "" : String(value);
  }

  function encodeForm(values) {
    return Object.keys(values)
      .filter(function(key) {
        return values[key] !== undefined && values[key] !== null && String(values[key]).length > 0;
      })
      .map(function(key) {
        return encodeURIComponent(key) + "=" + encodeURIComponent(String(values[key]));
      })
      .join("&");
  }

  function normalizeScopes(scopes) {
    if (!scopes) {
      return [];
    }

    if (Array.isArray(scopes)) {
      return scopes.map(asString).filter(function(scope) {
        return scope.length > 0;
      });
    }

    return String(scopes)
      .split(" ")
      .map(function(scope) {
        return scope.trim();
      })
      .filter(function(scope) {
        return scope.length > 0;
      });
  }

  function exchangeEveAuthorizationCode(code, codeVerifier, redirectUri, clientId, clientSecret) {
    var form = {
      grant_type: "authorization_code",
      code: code,
      redirect_uri: redirectUri,
      client_id: clientId
    };

    if (codeVerifier) {
      form.code_verifier = codeVerifier;
    }

    if (!codeVerifier && clientSecret) {
      form.client_secret = clientSecret;
    }

    var response = $http.send({
      url: EVE_TOKEN_URL,
      method: "POST",
      body: encodeForm(form),
      headers: {
        "content-type": "application/x-www-form-urlencoded"
      },
      timeout: 30
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
    var claims = $security.parseUnverifiedJWT(accessToken);
    var sub = asString(claims.sub);
    var characterId = sub.split(":").pop();
    var characterName = asString(claims.name);

    if (!characterId || !characterName) {
      throw new BadRequestError("EVE SSO token did not contain a character identity.");
    }

    return {
      character_id: characterId,
      character_name: characterName,
      scopes: normalizeScopes(claims.scp)
    };
  }

  function resolvePocketBaseUser(identity) {
    var requestAuth = e.requestInfo().auth;
    if (requestAuth && requestAuth.collection().name === "users") {
      return requestAuth;
    }

    try {
      var existingAccount = $app.findFirstRecordByData("eve_accounts", "character_id", identity.character_id);
      return $app.findRecordById("users", existingAccount.getString("user"));
    } catch (_) {
    }

    var users = $app.findCollectionByNameOrId("users");
    var user = new Record(users);
    user.setEmail("eve-" + identity.character_id + "@eve.local");
    user.setVerified(true);
    user.setRandomPassword();
    $app.save(user);
    return user;
  }

  function upsertEveAccount(user, identity, token, tokenEncryptionKey) {
    var account = null;
    try {
      account = $app.findFirstRecordByData("eve_accounts", "character_id", identity.character_id);
    } catch (err) {
      var collection = $app.findCollectionByNameOrId("eve_accounts");
      account = new Record(collection);
      account.set("character_id", identity.character_id);
    }

    var ownerId = account.getString("user");
    if (ownerId && ownerId !== user.id) {
      throw new BadRequestError("This EVE character is already linked to another user.");
    }

    var expiresIn = Number(token.expires_in || 1200);
    var expiresAt = new Date(Date.now() + expiresIn * 1000).toISOString();
    var tokenBundle = {
      access_token: asString(token.access_token),
      refresh_token: asString(token.refresh_token),
      token_type: asString(token.token_type) || "Bearer",
      expires_at: expiresAt
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

  try {
    var body = e.requestInfo().body || {};
    var code = asString(body.code);
    var codeVerifier = asString(body.code_verifier);
    var redirectUri = asString(body.redirect_uri) || $os.getenv("EVE_SSO_REDIRECT_URI");
    var clientId = $os.getenv("EVE_SSO_CLIENT_ID");
    var clientSecret = $os.getenv("EVE_SSO_CLIENT_SECRET");
    var tokenEncryptionKey = $os.getenv("TOKEN_ENCRYPTION_KEY");

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

    var eveToken = exchangeEveAuthorizationCode(code, codeVerifier, redirectUri, clientId, clientSecret);
    var identity = readEveIdentity(eveToken.access_token);
    var authUser = resolvePocketBaseUser(identity);
    var eveAccount = upsertEveAccount(authUser, identity, eveToken, tokenEncryptionKey);

    return e.json(200, {
      token: authUser.newAuthToken(),
      record: authUser.publicExport(),
      eve_account: eveAccount.publicExport()
    });
  } catch (err) {
    return e.json(400, {
      error: "eve_auth_failed",
      message: String(err && err.message ? err.message : err)
    });
  }
});
