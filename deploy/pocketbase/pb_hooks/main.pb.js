routerAdd("POST", "/api/eve-industry/auth/eve/callback", function(e) {
  return require(__hooks + "/eve-industry.js")().routeAuthCallback(e);
});

routerAdd("GET", "/api/eve-industry/auth/eve/access-token/{characterId}", function(e) {
  return require(__hooks + "/eve-industry.js")().routeAccessToken(e);
}, $apis.requireAuth("users"));

routerAdd("GET", "/api/eve-industry/data/snapshot", function(e) {
  return require(__hooks + "/eve-industry.js")().routeDataSnapshot(e);
}, $apis.requireAuth("users"));

routerAdd("PUT", "/api/eve-industry/data/settings", function(e) {
  return require(__hooks + "/eve-industry.js")().routeSaveSettings(e);
}, $apis.requireAuth("users"));

routerAdd("PUT", "/api/eve-industry/data/facilities", function(e) {
  return require(__hooks + "/eve-industry.js")().routeSaveFacilities(e);
}, $apis.requireAuth("users"));

routerAdd("PUT", "/api/eve-industry/data/production-ledger", function(e) {
  return require(__hooks + "/eve-industry.js")().routeSaveProductionLedger(e);
}, $apis.requireAuth("users"));

routerAdd("DELETE", "/api/eve-industry/data/production-ledger/{ledgerId}", function(e) {
  return require(__hooks + "/eve-industry.js")().routeDeleteProductionLedger(e);
}, $apis.requireAuth("users"));

routerAdd("PUT", "/api/eve-industry/data/market-scan-cache", function(e) {
  return require(__hooks + "/eve-industry.js")().routeSaveMarketScanCache(e);
}, $apis.requireAuth("users"));
