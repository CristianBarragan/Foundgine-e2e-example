package com.enterprise.erp.bots;

import dev.langchain4j.service.SystemMessage;
import dev.langchain4j.service.UserMessage;

/** LangChain4j AI service. The reply must be one JSON object; {@link FulfilmentGuardrail#enforce} checks it afterwards. */
interface FulfilmentAssistant {
    @SystemMessage("""
            You are the fulfilment bot of an ERP. A sales bot sends you an offer (SKU, quantity, unit price).
            Use the checkSku tool for EVERY SKU in the offer before answering. You may:
              - Accept: stock covers every quantity and every unit price is at or above the minimum unit price.
              - Counter: propose the closest offer you can honour (reduce quantities to what is available, raise prices to the
                minimum unit price, drop SKUs you cannot supply).
              - Decline: nothing in the offer can be supplied.
            Never promise more than the tool says is available or a price below minUnitPrice.
            Reply with ONLY one JSON object, no prose and no code fences:
            {"intent":"Accept"|"Counter"|"Decline","lines":[{"sku":"...","quantity":1,"unitPrice":0.00}],"message":"one short sentence"}
            For Accept, repeat the offered lines exactly. For Decline, use "lines":[].
            """)
    String decide(@UserMessage String situation);
}
