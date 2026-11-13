package com.enterprise.erp.bots;

import java.math.BigDecimal;

/** One offered line; mirrors contracts/schemas/bot-message.v1.json#/properties/lines. */
public record BotLine(String sku, int quantity, BigDecimal unitPrice) {}
