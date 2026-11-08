-- Phase 7: a quote whose ERP order could not be created (validation, pricing, stock) is marked OrderFailed with the reason.
ALTER TABLE quotes DROP CONSTRAINT quotes_status_check;
ALTER TABLE quotes ADD CONSTRAINT quotes_status_check
    CHECK (status IN ('Draft','PendingApproval','Approved','Rejected','Accepted','Ordered','OrderFailed'));
ALTER TABLE quotes ADD COLUMN order_failure_reason varchar(500) NOT NULL DEFAULT '';
