package com.enterprise.erp.order;

/** A summed quantity per SKU: the result row of the "ordered" and "already returned" JPQL constructor queries. */
public record SkuQuantity(String sku, Long quantity) {}
