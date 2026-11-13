\set ON_ERROR_STOP on
insert into erp_customers(erp_customer_id,tenant_id,crm_customer_id,kind,name,status) values('C1','t',1,'Business','x','Active');
insert into sales_orders(erp_order_id,tenant_id,crm_quote_id,erp_customer_id,total,status,correlation_id) values('SO1','t',77,'C1',10,'Open','c');
insert into erp_temporal_event_outbox(event_id,tenant_id,crm_quote_id,erp_order_id,event_type,entity_id,entity_version,payload)
 select gen_random_uuid(),'t',77,'SO1','shipment','S'||g,1,'{}' from generate_series(1,120) g;
insert into erp_temporal_event_outbox(event_id,tenant_id,crm_quote_id,erp_order_id,event_type,entity_id,entity_version,payload)
 values(gen_random_uuid(),'t',77,'SO1','shipment','S1',1,'{}') on conflict (tenant_id,event_type,entity_id,entity_version) do update set event_id=erp_temporal_event_outbox.event_id;
select 'rows_after_duplicate', count(*) from erp_temporal_event_outbox;
create temp table c1 as with x as (update erp_temporal_event_outbox set locked_until=now()+interval '30 seconds',attempts=attempts+1
 where event_id in (select event_id from erp_temporal_event_outbox where published_at is null and available_at<=now() and (locked_until is null or locked_until<now()) order by created_at for update skip locked limit 50) returning event_id) select * from x;
create temp table c2 as with x as (update erp_temporal_event_outbox set locked_until=now()+interval '30 seconds',attempts=attempts+1
 where event_id in (select event_id from erp_temporal_event_outbox where published_at is null and available_at<=now() and (locked_until is null or locked_until<now()) order by created_at for update skip locked limit 50) returning event_id) select * from x;
select 'claim1',count(*) from c1; select 'claim2',count(*) from c2; select 'overlap',count(*) from c1 join c2 using(event_id);
select 'claimable_while_leased', count(*) from erp_temporal_event_outbox where published_at is null and available_at<=now() and (locked_until is null or locked_until<now());
update erp_temporal_event_outbox set locked_until=now()-interval '1 second';
select 'claimable_after_lease_expiry', count(*) from erp_temporal_event_outbox where published_at is null and available_at<=now() and (locked_until is null or locked_until<now());
-- failure path backoff for a row claimed once (attempts=1): 2^1 = 2s
update erp_temporal_event_outbox set locked_until=null, available_at=now()+(least(300,power(2,least(attempts,8))::integer)*interval '1 second') where entity_id='S1';
select 'backoff_s_attempts1', round(extract(epoch from available_at-now())) from erp_temporal_event_outbox where entity_id='S1';
select 'unavailable_during_backoff', count(*) from erp_temporal_event_outbox where entity_id='S1' and available_at<=now();
