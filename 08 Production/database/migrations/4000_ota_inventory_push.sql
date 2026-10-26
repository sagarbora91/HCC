-- HCC migration 4000 · availability changes reach the OTA channel manager (audit A08-03, Wave D). Every change of the type-night counts
-- (reservations, holds and their expiry sweep, stay extend / shorten / early departure / room move, group blocks, room and maintenance
-- blocks) now publishes one InventoryChanged event per room type and transaction (Hcc.Infrastructure.SqlServer.Inventory.InventoryChangePublisher).
-- The OTA projector turns it into an automatic availability push of those nights to every Connected channel that maps the room type: an
-- ota.push row with origin 'Inventory', status Queued, delivered by the existing ChannelPushQueued handling. While a channel's automatic push
-- is still Queued, further changes are merged into it (one push, not one per change). Staff pushes (PushChannelRates) keep origin 'Staff'.
-- SIMULATED channel manager: nothing leaves the server.
ALTER TABLE ota.push ADD origin VARCHAR(10) NOT NULL CONSTRAINT DF_ota_push_origin DEFAULT 'Staff'
  CONSTRAINT CK_ota_push_origin CHECK (origin IN ('Staff','Inventory'));
GO
CREATE INDEX IX_ota_push_queued ON ota.push(channel_id, origin, push_id) WHERE status = 'Queued';
GO
