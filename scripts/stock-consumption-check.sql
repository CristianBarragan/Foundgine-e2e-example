\set ON_ERROR_STOP on
insert into erp_customers(erp_customer_id,tenant_id,crm_customer_id,kind,name,status) values('C1','t',1,'Business','x','Active');
insert into sales_orders(erp_order_id,tenant_id,crm_quote_id,erp_customer_id,total,status,correlation_id) values('SO1','t',1,'C1',10,'Open','c1'),('SO2','t',2,'C1',10,'Open','c2');
insert into erp_products(tenant_id,sku,name,list_price,min_unit_price) values('t','SKU-X','X',10,5),('t','SKU-Y','Y',10,5);
insert into erp_inventory(tenant_id,sku,on_hand,reserved) values('t','SKU-X',10,4),('t','SKU-Y',10,3);
insert into stock_reservations(erp_order_id,tenant_id,sku,quantity) values('SO1','t','SKU-X',4),('SO2','t','SKU-Y',3);
create function consume(o text) returns int language plpgsql as $$
declare h record; n int := 0;
begin
  for h in select sku,quantity from stock_reservations where tenant_id='t' and erp_order_id=o and released=false and consumed=false order by sku for update loop
    update erp_inventory set on_hand=on_hand-h.quantity, reserved=reserved-h.quantity where tenant_id='t' and sku=h.sku and reserved>=h.quantity;
    update stock_reservations set consumed=true where erp_order_id=o and sku=h.sku;
    n := n + 1;
  end loop;
  return n;
end $$;
select 'consumed_first_pass', consume('SO1');
select 'consumed_second_pass', consume('SO1');
select 'SKU-X_on_hand_reserved', on_hand||','||reserved from erp_inventory where sku='SKU-X';
select 'cancel_releasable_rows_after_consume', count(*) from stock_reservations where erp_order_id='SO1' and released=false and consumed=false;
select 'cancel_releasable_rows_unshipped', count(*) from stock_reservations where erp_order_id='SO2' and released=false and consumed=false;
update erp_inventory set reserved=reserved-3 where sku='SKU-Y'; update stock_reservations set released=true where erp_order_id='SO2';
select 'SKU-Y_on_hand_reserved', on_hand||','||reserved from erp_inventory where sku='SKU-Y';
do $$ begin
  begin update erp_inventory set reserved=reserved-4 where sku='SKU-X'; raise exception 'expected CHECK violation (reserved >= 0)';
  exception when check_violation then raise notice 'OK: negative reserved rejected'; end;
  begin update stock_reservations set released=true where erp_order_id='SO1'; raise exception 'expected CHECK violation (released xor consumed)';
  exception when check_violation then raise notice 'OK: released+consumed rejected'; end;
end $$;
