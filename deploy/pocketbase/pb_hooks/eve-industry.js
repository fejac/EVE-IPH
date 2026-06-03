module.exports = function() {
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

  function tokenEncryptionKey() {
    var key = $os.getenv("TOKEN_ENCRYPTION_KEY");
    if (!key || key.length !== 32) {
      throw new BadRequestError("Server TOKEN_ENCRYPTION_KEY must be exactly 32 characters.");
    }

    return key;
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

  function refreshEveToken(account, token, key) {
    var clientId = $os.getenv("EVE_SSO_CLIENT_ID");
    var clientSecret = $os.getenv("EVE_SSO_CLIENT_SECRET");
    if (!clientId) {
      throw new BadRequestError("Server is missing EVE_SSO_CLIENT_ID.");
    }

    if (!token.refresh_token) {
      throw new BadRequestError("Stored EVE token does not contain a refresh token.");
    }

    var form = {
      grant_type: "refresh_token",
      refresh_token: token.refresh_token,
      client_id: clientId
    };

    if (clientSecret) {
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
      throw new BadRequestError("EVE SSO token refresh failed: " + String(response.raw || response.body || response.statusCode));
    }

    if (!response.json || !response.json.access_token) {
      throw new BadRequestError("EVE SSO refresh response did not contain an access token.");
    }

    var expiresIn = Number(response.json.expires_in || 1200);
    var refreshed = {
      access_token: asString(response.json.access_token),
      refresh_token: asString(response.json.refresh_token) || asString(token.refresh_token),
      token_type: asString(response.json.token_type) || "Bearer",
      expires_at: new Date(Date.now() + expiresIn * 1000).toISOString()
    };

    saveTokenBundle(account, refreshed, key);
    return refreshed;
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

  function resolvePocketBaseUser(e, identity) {
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

  function saveTokenBundle(account, tokenBundle, key) {
    account.set("token_ciphertext", $security.encrypt(JSON.stringify(tokenBundle), key));
    account.set("token_expires_at", tokenBundle.expires_at);
    $app.save(account);
  }

  function upsertEveAccount(user, identity, token, key) {
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
    saveTokenBundle(account, tokenBundle, key);
    return account;
  }

  function requireUser(e) {
    var auth = e.requestInfo().auth;
    if (!auth || auth.collection().name !== "users") {
      throw new UnauthorizedError("Authentication required.");
    }

    return auth;
  }

  function findOwnedAccount(e, characterId) {
    var user = requireUser(e);
    var account = $app.findFirstRecordByData("eve_accounts", "character_id", asString(characterId));
    if (account.getString("user") !== user.id) {
      throw new ForbiddenError("This EVE character is not linked to the authenticated user.");
    }

    return account;
  }

  function readTokenBundle(account, key) {
    var encrypted = account.getString("token_ciphertext");
    if (!encrypted) {
      throw new BadRequestError("Stored EVE token is missing.");
    }

    return JSON.parse(String($security.decrypt(encrypted, key)));
  }

  function getFreshToken(account) {
    var key = tokenEncryptionKey();
    var token = readTokenBundle(account, key);
    var expiresAt = Date.parse(asString(token.expires_at));
    if (!expiresAt || expiresAt <= Date.now() + 60000) {
      token = refreshEveToken(account, token, key);
    }

    return token;
  }

  function routeAuthCallback(e) {
    try {
      var body = e.requestInfo().body || {};
      var code = asString(body.code);
      var codeVerifier = asString(body.code_verifier);
      var redirectUri = asString(body.redirect_uri) || $os.getenv("EVE_SSO_REDIRECT_URI");
      var clientId = $os.getenv("EVE_SSO_CLIENT_ID");
      var clientSecret = $os.getenv("EVE_SSO_CLIENT_SECRET");
      var key = tokenEncryptionKey();

      if (!code) {
        return e.json(400, { error: "missing_code", message: "Missing EVE SSO authorization code." });
      }

      if (!redirectUri) {
        return e.json(400, { error: "missing_redirect_uri", message: "Missing EVE SSO redirect URI." });
      }

      if (!clientId) {
        return e.json(400, { error: "missing_client_id", message: "Server is missing EVE_SSO_CLIENT_ID." });
      }

      var eveToken = exchangeEveAuthorizationCode(code, codeVerifier, redirectUri, clientId, clientSecret);
      var identity = readEveIdentity(eveToken.access_token);
      var authUser = resolvePocketBaseUser(e, identity);
      var eveAccount = upsertEveAccount(authUser, identity, eveToken, key);

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
  }

  function routeAccessToken(e) {
    var characterId = e.request.pathValue("characterId");
    var account = findOwnedAccount(e, characterId);
    var token = getFreshToken(account);

    return e.json(200, {
      access_token: token.access_token,
      token_type: token.token_type || "Bearer",
      expires_at: token.expires_at
    });
  }

  function findFirstOwned(collectionName, userId) {
    try {
      return $app.findFirstRecordByData(collectionName, "user", userId);
    } catch (_) {
      return null;
    }
  }

  function findOwnedRecords(collectionName, userId) {
    return $app.findRecordsByFilter(collectionName, "user = '" + String(userId).replace(/'/g, "''") + "'", "", 200, 0);
  }

  function saveSingletonJson(collectionName, jsonField, userId, value) {
    var record = findFirstOwned(collectionName, userId);
    if (!record) {
      record = new Record($app.findCollectionByNameOrId(collectionName));
      record.set("user", userId);
    }

    record.set(jsonField, JSON.parse(JSON.stringify(value || {})));
    $app.save(record);
    return record;
  }

  function routeDataSnapshot(e) {
    var user = requireUser(e);
    var settings = findFirstOwned("user_settings", user.id);
    var ledgers = findOwnedRecords("production_ledgers", user.id).map(function(record) {
      return {
        id: record.id,
        name: record.getString("name"),
        ledger: record.get("ledger")
      };
    });
    var facilities = findOwnedRecords("facilities", user.id).map(function(record) {
      return record.get("profile");
    });
    var marketCache = findOwnedRecords("market_scan_cache", user.id).map(function(record) {
      return {
        cache_key: record.getString("cache_key"),
        expires_at: record.getString("expires_at"),
        rows: record.get("rows")
      };
    });

    return e.json(200, {
      settings: settings ? settings.get("settings") : null,
      facilities: facilities,
      production_ledgers: ledgers,
      market_scan_cache: marketCache
    });
  }

  function routeSaveSettings(e) {
    try {
      var user = requireUser(e);
      var body = e.requestInfo().body || {};
      saveSingletonJson("user_settings", "settings", user.id, body.settings || body);
      return e.json(200, { ok: true });
    } catch (err) {
      return e.json(400, {
        error: "settings_save_failed",
        message: String(err && err.message ? err.message : err)
      });
    }
  }

  function routeSaveFacilities(e) {
    try {
      var user = requireUser(e);
      var body = e.requestInfo().body || {};
      var profiles = body.facilities || [];
      var existing = findOwnedRecords("facilities", user.id);
      existing.forEach(function(record) {
        $app.delete(record);
      });

      profiles.forEach(function(profile) {
        var record = new Record($app.findCollectionByNameOrId("facilities"));
        record.set("user", user.id);
        record.set("name", asString(profile.Name || profile.name || "Facility"));
        record.set("profile", JSON.parse(JSON.stringify(profile)));
        $app.save(record);
      });

      return e.json(200, { ok: true });
    } catch (err) {
      return e.json(400, {
        error: "facilities_save_failed",
        message: String(err && err.message ? err.message : err)
      });
    }
  }

  function routeSaveProductionLedger(e) {
    var user = requireUser(e);
    var body = e.requestInfo().body || {};
    var ledgerId = asString(body.id);
    var record = null;
    if (ledgerId) {
      try {
        record = $app.findRecordById("production_ledgers", ledgerId);
      } catch (err) {
        record = null;
      }

      if (record && record.getString("user") !== user.id) {
        throw new ForbiddenError("This production ledger is not owned by the authenticated user.");
      }
    }

    if (!record) {
      record = new Record($app.findCollectionByNameOrId("production_ledgers"));
      record.set("user", user.id);
    }

    record.set("name", asString(body.name) || "Production Ledger");
    record.set("ledger", body.ledger || body);
    $app.save(record);
    return e.json(200, { ok: true, id: record.id });
  }

  function routeDeleteProductionLedger(e) {
    try {
      var user = requireUser(e);
      var ledgerId = asString(e.request.pathValue("ledgerId"));
      var record = $app.findRecordById("production_ledgers", ledgerId);
      if (record.getString("user") !== user.id) {
        throw new ForbiddenError("This production ledger is not owned by the authenticated user.");
      }

      $app.delete(record);
      return e.json(200, { ok: true });
    } catch (err) {
      return e.json(400, {
        error: "production_ledger_delete_failed",
        message: String(err && err.message ? err.message : err)
      });
    }
  }

  function routeSaveMarketScanCache(e) {
    var user = requireUser(e);
    var body = e.requestInfo().body || {};
    var cacheKey = asString(body.cache_key);
    if (!cacheKey) {
      throw new BadRequestError("Missing market scanner cache key.");
    }

    var existing = findOwnedRecords("market_scan_cache", user.id).filter(function(record) {
      return record.getString("cache_key") === cacheKey;
    })[0];

    var record = existing || new Record($app.findCollectionByNameOrId("market_scan_cache"));
    record.set("user", user.id);
    record.set("cache_key", cacheKey);
    record.set("expires_at", asString(body.expires_at));
    record.set("rows", body.rows || []);
    $app.save(record);
    return e.json(200, { ok: true });
  }

  return {
    routeAuthCallback: routeAuthCallback,
    routeAccessToken: routeAccessToken,
    routeDataSnapshot: routeDataSnapshot,
    routeSaveSettings: routeSaveSettings,
    routeSaveFacilities: routeSaveFacilities,
    routeSaveProductionLedger: routeSaveProductionLedger,
    routeDeleteProductionLedger: routeDeleteProductionLedger,
    routeSaveMarketScanCache: routeSaveMarketScanCache
  };
};
