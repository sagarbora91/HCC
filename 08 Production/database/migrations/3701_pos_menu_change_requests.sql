-- HCC migration 3701 · POS menu and price change requests (DEC-079 D5: PERM-POS-006 Owner; the Manager's cell is R, so a Manager's
-- SaveMenuItem stores a request here and the Owner decides it with DecideMenuChange). Same pattern as pos.void_request: one row per
-- request, never decided by the requester (PERM-SOD-001), bound to the item's rowversion when asked (a changed item makes it stale).
CREATE TABLE pos.menu_change_request(
  request_id BIGINT IDENTITY CONSTRAINT PK_pos_menu_request PRIMARY KEY,
  property_id INT NOT NULL CONSTRAINT FK_pos_menu_request_property REFERENCES inv.property,
  item_id INT NULL CONSTRAINT FK_pos_menu_request_item REFERENCES pos.menu_item,   -- NULL = a new item
  code VARCHAR(20) NOT NULL,
  name NVARCHAR(100) NOT NULL,
  category NVARCHAR(60) NOT NULL,
  price_paise BIGINT NOT NULL CONSTRAINT CK_pos_menu_request_price CHECK (price_paise BETWEEN 0 AND 10000000000),
  price_before_paise BIGINT NULL,
  veg BIT NOT NULL,
  outlet_ids VARCHAR(400) NOT NULL,          -- comma-separated outlet ids, as asked
  item_version BINARY(8) NULL,               -- the item's rowversion when asked (NULL for a new item)
  reason NVARCHAR(200) NULL,
  status VARCHAR(8) NOT NULL CONSTRAINT CK_pos_menu_request_status CHECK (status IN ('Pending','Rejected','Executed')),
  requested_by INT NOT NULL,
  requested_utc DATETIME2(3) NOT NULL,
  decided_by INT NULL,
  decided_utc DATETIME2(3) NULL,
  decision_reason NVARCHAR(200) NULL,
  row_ver ROWVERSION,
  CONSTRAINT CK_pos_menu_request_item CHECK ((item_id IS NULL AND item_version IS NULL AND price_before_paise IS NULL) OR (item_id IS NOT NULL AND item_version IS NOT NULL)),
  CONSTRAINT CK_pos_menu_request_sod CHECK (decided_by IS NULL OR decided_by <> requested_by),
  CONSTRAINT CK_pos_menu_request_decided CHECK ((status = 'Pending' AND decided_by IS NULL) OR (status <> 'Pending' AND decided_by IS NOT NULL AND decided_utc IS NOT NULL)));
-- One pending change per item code (a second request waits until the first is decided).
CREATE UNIQUE INDEX UX_pos_menu_request_pending ON pos.menu_change_request(property_id, code) WHERE status = 'Pending';
