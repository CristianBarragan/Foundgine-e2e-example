
import React from "react";
import { createRoot } from "react-dom/client";
import Keycloak from "keycloak-js";
import App from "./App";
import "./style.css";

const keycloak = new Keycloak({
  url: import.meta.env.VITE_KEYCLOAK_URL || "http://localhost:8080",
  realm: "foundgine-crm",
  clientId: "crm-web"
});

keycloak.init({ onLoad: "login-required", pkceMethod: "S256", checkLoginIframe: false })
  .then(authenticated => {
    if (!authenticated) return keycloak.login();
    createRoot(document.getElementById("root")!).render(
      <React.StrictMode><App keycloak={keycloak} /></React.StrictMode>
    );
  })
  .catch(error => {
    document.getElementById("root")!.innerText = `Login failed: ${String(error)}`;
  });
