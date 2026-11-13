package com.enterprise.orchestration;

import java.math.BigDecimal;
import java.util.List;

/**
 * Language-neutral contracts as seen by the orchestration worker (JSON camelCase; see contracts/schemas).
 * Deliberately copies of the CRM/ERP DTOs: the schemas are the contract, not shared binaries.
 */
public final class Contracts {
    private Contracts() {}

    public record CustomerSync(int schemaVersion, String tenantId, int crmCustomerId, int attempt, String correlationId,
                               String kind, String name, String email, String phone, String status) {}

    public record ErpCustomerResult(String erpCustomerId, boolean created) {}

    public record RecordErpCustomerInput(String tenantId, int crmCustomerId, String erpCustomerId) {}

    public record MarkSyncFailedInput(String tenantId, int crmCustomerId, String reason) {}

    public record OnboardingResult(String status, String erpCustomerId, String reason) {}

    // Quote-to-order (contracts/schemas/quote-accepted.v1.json)
    public record QuoteLine(String sku, String description, int quantity, BigDecimal unitPrice) {}

    public record QuoteAccepted(int schemaVersion, String tenantId, int quoteId, int opportunityId, int crmCustomerId,
                                String erpCustomerId, String correlationId, BigDecimal total, List<QuoteLine> lines) {}

    public record ErpOrderResult(String erpOrderId, boolean created) {}

    public record CancelOrderRequest(String tenantId, int quoteId, String erpOrderId, String reason) {}

    public record CancelOrderResult(String erpOrderId, boolean cancelled) {}

    public record RecordErpOrderInput(String tenantId, int quoteId, String erpOrderId) {}

    public record MarkOrderFailedInput(String tenantId, int quoteId, String reason) {}

    // Returns (contracts/schemas/return-requested.v1.json, fulfilment-activities.v1.json#recordReturnDecision)
    public record ReturnLine(String sku, int quantity) {}

    public record ReturnRequested(int schemaVersion, String tenantId, int returnId, int quoteId, int crmCustomerId,
                                  String erpCustomerId, String erpOrderId, String correlationId, String reason,
                                  List<ReturnLine> lines) {}

    public record ErpReturnDecision(String decision, String erpReturnId, String note, boolean created) {}

    public record CompleteReturnRequest(String tenantId, String erpReturnId) {}

    public record CompleteReturnResult(String erpReturnId, boolean completed) {}

    public record RecordReturnDecisionInput(String tenantId, int returnId, String decision, String erpReturnId, String note) {}

    /** Sent by the ERP endpoint when the warehouse has received the goods; receivedBy is the authenticated subject. */
    public record GoodsReceived(String receivedBy) {}

    /** Starts ReturnResolutionWorkflow: an operator confirms goods arrived after the 30-day wait expired. resolvedBy is the authenticated subject. */
    public record ResolveExpiredReturn(String tenantId, int returnId, String erpReturnId, String resolvedBy) {}

    /** status: Completed | Rejected | AwaitingGoodsExpired (still Authorized in both systems). */
    public record ReturnResult(String status, String erpReturnId, String reason) {}

    /** status: Completed | Failed. On Failed no ERP order or reservation remains. */
    public record QuoteToOrderResult(String status, String erpOrderId, String reason) {}

    public static final String ORCHESTRATION_QUEUE = "orchestration";
    public static final String CRM_QUEUE = "crm";
    public static final String ERP_QUEUE = "erp";

    public static final String CRM_RECORD_ERP_CUSTOMER = "crm.record-erp-customer";
    public static final String CRM_MARK_SYNC_FAILED = "crm.mark-sync-failed";
    public static final String ERP_VALIDATE_AND_CREATE = "erp.validate-and-create-customer";
    public static final String ERP_CREATE_ORDER = "erp.create-order-with-reservation";
    public static final String ERP_CANCEL_ORDER = "erp.cancel-order";
    public static final String CRM_RECORD_ERP_ORDER = "crm.record-erp-order";
    public static final String CRM_MARK_ORDER_FAILED = "crm.mark-order-failed";
    public static final String ERP_AUTHORIZE_RETURN = "erp.authorize-return";
    public static final String ERP_COMPLETE_RETURN = "erp.complete-return";
    public static final String CRM_RECORD_RETURN_DECISION = "crm.record-return-decision";

    // Sales <-> fulfilment bot negotiation (contracts/schemas/bot-*.v1.json). Advisory: nothing is written to CRM or ERP.
    public record BotLine(String sku, int quantity, BigDecimal unitPrice) {}

    /** from: sales | fulfilment. intent: Propose | Counter | Accept | Decline. */
    public record BotMessage(int schemaVersion, String from, int turn, String intent, List<BotLine> lines, String message) {}

    public record BriefLine(String sku, int quantity, int minQuantity, BigDecimal maxUnitPrice) {}

    /** The customer's wishes and limits. Only the sales bot ever receives it. */
    public record Brief(String note, List<BriefLine> lines) {}

    public record BotNegotiationStart(int schemaVersion, String tenantId, String correlationId, String negotiationId,
                                      int crmCustomerId, int maxTurns, Brief brief) {}

    /** brief is null for the fulfilment bot. history is every message so far, oldest first. */
    public record BotTurnInput(int schemaVersion, String tenantId, String correlationId, String negotiationId, int turn,
                               int crmCustomerId, Brief brief, List<BotMessage> history) {}

    /** status: Agreed | Declined | Exhausted. lines/total are empty/zero unless Agreed. */
    public record BotNegotiationResult(int schemaVersion, String negotiationId, String status, int turns,
                                       List<BotLine> lines, BigDecimal total, String reason, List<BotMessage> transcript) {}

    public static final String CRM_SALES_BOT_TURN = "crm.sales-bot.take-turn";
    public static final String ERP_FULFILMENT_BOT_TURN = "erp.fulfilment-bot.take-turn";
}
