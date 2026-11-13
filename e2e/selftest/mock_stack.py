#!/usr/bin/env python3
"""Minimal fake Keycloak + CRM + ERP (one port) so e2e/run.sh can be self-tested without Docker.
It mirrors only the response shapes run.sh reads. MOCK_NO_RESERVE=1 simulates the bug "stock is never reserved"."""
import json, os, re, sys
from http.server import BaseHTTPRequestHandler, HTTPServer

QTY = 5
S = {"reserved": 0, "onhand": 100, "shipped": False, "accept_calls": 0, "quote_gets": 0, "fulfil_gets": 0, "reconciled": False}

class H(BaseHTTPRequestHandler):
    def log_message(self, *a): pass
    def send(self, code, body):
        data = json.dumps(body).encode()
        self.send_response(code); self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data))); self.end_headers(); self.wfile.write(data)
    def body(self):
        n = int(self.headers.get("Content-Length") or 0); return self.rfile.read(n) if n else b""
    def authed(self):
        if not (self.headers.get("Authorization") or "").startswith("Bearer tok-"):
            self.send(401, {"error": "no token"}); return False
        return True

    def do_POST(self):
        p = self.path; raw = self.body()
        if p.endswith("/protocol/openid-connect/token"):
            user = re.search(r"username=(\w+)", raw.decode()).group(1)
            return self.send(200, {"access_token": f"tok-{user}"})
        if not self.authed(): return
        if p == "/api/v1/customers": return self.send(201, {"id": 1})
        if p == "/api/v1/customers/1/opportunities": return self.send(201, {"id": 2})
        if p == "/api/v1/opportunities/2/quotes":
            assert json.loads(raw)["lines"][0]["quantity"] == QTY
            return self.send(201, {"quote": {"id": 3}})
        if p == "/api/v1/quotes/3/submit": return self.send(200, {"quote": {"status": "Approved"}})
        if p == "/api/v1/quotes/3/accept":
            S["accept_calls"] += 1
            return self.send(409, {"error": "customer not yet synchronised"}) if S["accept_calls"] < 3 else self.send(200, {"quote": {"status": "Accepted"}})
        if p == "/api/v1/orders/SO-5001/shipments": return self.send(201, {"erpShipmentId": "SHP-1"})
        if re.fullmatch(r"/api/v1/shipments/SHP-1/transitions", p):
            nxt = json.loads(raw)["status"]
            if nxt == "Shipped" and not S["shipped"] and not os.environ.get("MOCK_NO_CONSUME"):
                S["shipped"] = True; S["onhand"] -= QTY; S["reserved"] -= QTY   # shipping consumes the reservation, once
            return self.send(200, {"status": nxt})
        if p == "/api/v1/orders/SO-5001/invoices": return self.send(201, {"erpInvoiceId": "INV-1", "total": 500.00})
        if p == "/api/v1/invoices/INV-1/payments": return self.send(201, {"erpPaymentId": "PAY-1"})
        if p == "/api/v1/payments/PAY-1/reconcile": S["reconciled"] = True; return self.send(200, {})
        self.send(404, {"error": p})

    def do_GET(self):
        p = self.path
        if not self.authed(): return
        if p == "/api/v1/inventory/SKU-1":
            return self.send(200, {"sku": "SKU-1", "onHand": S["onhand"], "reserved": S["reserved"], "available": S["onhand"] - S["reserved"]})
        if p == "/api/v1/quotes/3":
            S["quote_gets"] += 1
            if S["quote_gets"] < 3: return self.send(200, {"quote": {"status": "Accepted", "erpOrderId": ""}})
            if not os.environ.get("MOCK_NO_RESERVE"): S["reserved"] = QTY
            return self.send(200, {"quote": {"status": "Ordered", "erpOrderId": "SO-5001"}})
        if p == "/api/v1/quotes/3/fulfilment":
            S["fulfil_gets"] += 1
            done = S["reconciled"] and S["fulfil_gets"] >= 3
            return self.send(200, {"shipments": [{"erpShipmentId": "SHP-1", "status": "Delivered" if done else "Packed", "version": 4 if done else 1}],
                                   "invoices": [{"erpInvoiceId": "INV-1", "status": "Paid" if done else "Issued", "version": 2}],
                                   "payments": [{"erpPaymentId": "PAY-1", "status": "Reconciled", "version": 2}]})
        self.send(404, {"error": p})

if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 18080
    HTTPServer(("127.0.0.1", port), H).serve_forever()
