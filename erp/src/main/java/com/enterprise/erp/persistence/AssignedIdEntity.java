package com.enterprise.erp.persistence;

import jakarta.persistence.MappedSuperclass;
import jakarta.persistence.PostLoad;
import jakarta.persistence.PostPersist;
import jakarta.persistence.Transient;
import org.springframework.data.domain.Persistable;

/**
 * Base for entities whose String id the application assigns (SHP-..., INV-..., PAY-...). Without this, Spring Data sees a
 * non-null id, treats save() as a merge, runs a pointless SELECT first and hands back a different (managed) instance than
 * the one passed in. Reporting a fresh instance as new makes save() a plain persist, so the instance passed in stays the
 * managed one.
 */
@MappedSuperclass
public abstract class AssignedIdEntity implements Persistable<String> {
    @Transient private boolean isNew = true;

    @Override public boolean isNew() { return isNew; }

    @PostPersist @PostLoad void markNotNew() { this.isNew = false; }
}
