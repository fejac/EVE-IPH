migrate((app) => {
  const userOwnRule = "id = @request.auth.id";
  const recordOwnRule = "user = @request.auth.id";

  const users = app.findCollectionByNameOrId("users");
  users.listRule = userOwnRule;
  users.viewRule = userOwnRule;
  users.createRule = null;
  users.updateRule = userOwnRule;
  users.deleteRule = userOwnRule;
  app.save(users);

  function ownedRelationField() {
    return {
      name: "user",
      type: "relation",
      required: true,
      maxSelect: 1,
      collectionId: users.id,
      cascadeDelete: true
    };
  }

  app.save(new Collection({
    type: "base",
    name: "eve_accounts",
    listRule: recordOwnRule,
    viewRule: recordOwnRule,
    createRule: recordOwnRule,
    updateRule: recordOwnRule,
    deleteRule: recordOwnRule,
    fields: [
      ownedRelationField(),
      {
        name: "character_id",
        type: "text",
        required: true,
        max: 32,
        presentable: true
      },
      {
        name: "character_name",
        type: "text",
        required: true,
        max: 120
      },
      {
        name: "corporation_id",
        type: "text",
        max: 32
      },
      {
        name: "scopes",
        type: "json"
      },
      {
        name: "token_ciphertext",
        type: "text"
      },
      {
        name: "token_expires_at",
        type: "date"
      }
    ],
    indexes: [
      "CREATE UNIQUE INDEX idx_eve_accounts_character_id ON eve_accounts (character_id)"
    ]
  }));

  app.save(new Collection({
    type: "base",
    name: "user_settings",
    listRule: recordOwnRule,
    viewRule: recordOwnRule,
    createRule: recordOwnRule,
    updateRule: recordOwnRule,
    deleteRule: recordOwnRule,
    fields: [
      ownedRelationField(),
      {
        name: "settings",
        type: "json",
        required: true
      }
    ],
    indexes: [
      "CREATE UNIQUE INDEX idx_user_settings_user ON user_settings (user)"
    ]
  }));

  app.save(new Collection({
    type: "base",
    name: "facilities",
    listRule: recordOwnRule,
    viewRule: recordOwnRule,
    createRule: recordOwnRule,
    updateRule: recordOwnRule,
    deleteRule: recordOwnRule,
    fields: [
      ownedRelationField(),
      {
        name: "name",
        type: "text",
        required: true,
        max: 120,
        presentable: true
      },
      {
        name: "profile",
        type: "json",
        required: true
      }
    ],
    indexes: [
      "CREATE INDEX idx_facilities_user_name ON facilities (user, name)"
    ]
  }));

  app.save(new Collection({
    type: "base",
    name: "production_ledgers",
    listRule: recordOwnRule,
    viewRule: recordOwnRule,
    createRule: recordOwnRule,
    updateRule: recordOwnRule,
    deleteRule: recordOwnRule,
    fields: [
      ownedRelationField(),
      {
        name: "name",
        type: "text",
        required: true,
        max: 120,
        presentable: true
      },
      {
        name: "ledger",
        type: "json",
        required: true
      }
    ],
    indexes: [
      "CREATE INDEX idx_production_ledgers_user_updated ON production_ledgers (user, updated)"
    ]
  }));

  app.save(new Collection({
    type: "base",
    name: "market_scan_cache",
    listRule: recordOwnRule,
    viewRule: recordOwnRule,
    createRule: recordOwnRule,
    updateRule: recordOwnRule,
    deleteRule: recordOwnRule,
    fields: [
      ownedRelationField(),
      {
        name: "cache_key",
        type: "text",
        required: true,
        max: 255
      },
      {
        name: "expires_at",
        type: "date",
        required: true
      },
      {
        name: "rows",
        type: "json",
        required: true
      }
    ],
    indexes: [
      "CREATE UNIQUE INDEX idx_market_scan_cache_user_key ON market_scan_cache (user, cache_key)",
      "CREATE INDEX idx_market_scan_cache_expires_at ON market_scan_cache (expires_at)"
    ]
  }));
}, (app) => {
  [
    "market_scan_cache",
    "production_ledgers",
    "facilities",
    "user_settings",
    "eve_accounts"
  ].forEach((name) => {
    try {
      const collection = app.findCollectionByNameOrId(name);
      app.delete(collection);
    } catch (_) {
    }
  });

  try {
    const users = app.findCollectionByNameOrId("users");
    users.listRule = null;
    users.viewRule = null;
    users.createRule = null;
    users.updateRule = null;
    users.deleteRule = null;
    app.save(users);
  } catch (_) {
  }
});
