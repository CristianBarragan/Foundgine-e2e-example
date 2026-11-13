package com.enterprise.erp.returns;

import java.util.List;
import org.springframework.data.jpa.repository.JpaRepository;

public interface ErpReturnLineRepository extends JpaRepository<ErpReturnLine, ErpReturnLine.Key> {
    List<ErpReturnLine> findByErpReturnIdOrderBySku(String erpReturnId);
}
