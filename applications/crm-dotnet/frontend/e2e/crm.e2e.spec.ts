import { test, expect, type Page } from "@playwright/test";

async function signIn(page: Page, username: string, password: string) {
  await page.goto("/");
  await page.locator("#username").waitFor({ state: "visible", timeout: 30_000 });
  await page.locator("#username").fill(username);
  await page.locator("#password").fill(password);
  await page.locator("#kc-login").click();
  await expect(page.getByRole("heading", { name: "Overview" })).toBeVisible({ timeout: 30_000 });
}

test("Keycloak login and tenant isolation survive a real UI-to-PostgreSQL journey", async ({ browser, request }) => {
  const unauthenticated = await request.get("http://localhost:5080/api/companies");
  expect(unauthenticated.status()).toBe(401);

  const tenantA = await browser.newContext();
  const pageA = await tenantA.newPage();
  const uniqueCompany = `Tenant A E2E ${Date.now()}`;
  await signIn(pageA, "sales", "ChangeMe123!");
  pageA.once("dialog", dialog => dialog.accept(uniqueCompany));
  await pageA.getByRole("button", { name: /Add company/ }).first().click();
  await pageA.getByRole("button", { name: "Companies", exact: true }).click();
  await expect(pageA.getByRole("cell", { name: uniqueCompany })).toBeVisible();

  const tenantB = await browser.newContext();
  const pageB = await tenantB.newPage();
  await signIn(pageB, "sales-other", "ChangeMe123!");
  await pageB.getByRole("button", { name: "Companies", exact: true }).click();
  await expect(pageB.getByRole("cell", { name: uniqueCompany })).toHaveCount(0);
  pageB.once("dialog", dialog => dialog.accept(`Denied write ${Date.now()}`));
  await pageB.getByRole("button", { name: /Add company/ }).first().click();
  await expect(pageB.locator(".error")).toContainText("403");

  await tenantA.close();
  await tenantB.close();
});

test("AI chat answers through the read-only Foundgine query tool", async ({ page }) => {
  test.skip(!process.env.OPENAI_API_KEY, "Set OPENAI_API_KEY in the test environment and API container to run the AI integration test.");
  await signIn(page, "sales", "ChangeMe123!");
  await page.getByRole("button", { name: /Ask AI/ }).click();
  await page.getByRole("textbox", { name: "Message the CRM assistant" }).fill("How many companies are in my CRM?");
  await page.getByRole("button", { name: "Send" }).click();
  await expect(page.locator(".chat-bubble.assistant")).toHaveCount(2, { timeout: 60_000 });
  await expect(page.locator(".assistant-error")).toHaveCount(0);
});
