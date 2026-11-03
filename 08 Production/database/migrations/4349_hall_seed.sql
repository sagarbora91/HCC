-- HCC migration 4349 · F6 hall booking seed (lane H2; spec §4, §5, §7, §12): for every property that has no halls yet — two halls and one lawn
-- (fictional names and rates, before GST), the T&C templates General / Wedding / Corporate with every clause text "OWNER TO REPLACE" (a quotation
-- cannot be sent with them until the Owner replaces the text or overrides with a reason), the lost reasons and the three menu packages.
-- Rates and names are illustrative; the Owner edits them in the halls master. A property added later starts empty (the commands then use the
-- built-in lost reasons and the placeholder General template).
DECLARE @props TABLE(property_id INT PRIMARY KEY);
INSERT @props SELECT p.property_id FROM inv.property p WHERE NOT EXISTS (SELECT 1 FROM hall.venue v WHERE v.property_id = p.property_id);

INSERT hall.venue(property_id, code, name, kind, cap_theatre, cap_round, cap_ushape, cap_floating, cap_classroom, min_plates, gst_class)
SELECT property_id, x.code, x.name, x.kind, x.t, x.r, x.u, x.f, x.c, x.mp, 'HALL_RENT' FROM @props
CROSS JOIN (VALUES ('V-HALL-A', N'Banquet Hall A (fictional)', 'HALL', 200, 150, 60, 250, 100, 100),
                   ('V-HALL-B', N'Banquet Hall B (fictional)', 'HALL', 80, 60, 30, 100, 40, NULL),
                   ('V-LAWN-1', N'Garden Lawn (fictional seed)', 'LAWN', NULL, 300, NULL, 500, NULL, 150)) x(code, name, kind, t, r, u, f, c, mp);

INSERT hall.venue_slot(venue_id, slot, time_from, time_to, rate_paise)
SELECT v.venue_id, s.slot, s.f, s.t, s.rate FROM hall.venue v JOIN @props p ON p.property_id = v.property_id
JOIN (VALUES ('V-HALL-A', 'MORNING', '08:00', '13:00', 1000000), ('V-HALL-A', 'EVENING', '18:00', '23:00', 1500000), ('V-HALL-A', 'FULL_DAY', '08:00', '23:00', 2500000),
             ('V-HALL-B', 'MORNING', '08:00', '13:00', 500000), ('V-HALL-B', 'AFTERNOON', '13:00', '17:00', 500000), ('V-HALL-B', 'EVENING', '18:00', '23:00', 800000),
             ('V-HALL-B', 'FULL_DAY', '08:00', '23:00', 1300000), ('V-LAWN-1', 'EVENING', '18:00', '23:00', 2500000), ('V-LAWN-1', 'FULL_DAY', '08:00', '23:00', 4000000)) s(code, slot, f, t, rate)
  ON s.code = v.code;

INSERT hall.lost_reason(property_id, reason_id, label, needs_note, sort_order)
SELECT p.property_id, x.id, x.label, x.nn, x.so FROM @props p
CROSS JOIN (VALUES ('LR-PRICE', N'Price too high', 0, 10), ('LR-DATE', N'Date not available', 0, 20), ('LR-OTHERVENUE', N'Chose another venue', 0, 30),
                   ('LR-CANCELLED', N'Event cancelled', 0, 40), ('LR-NORESPONSE', N'No response', 0, 50), ('LR-OTHER', N'Other', 1, 60)) x(id, label, nn, so)
WHERE NOT EXISTS (SELECT 1 FROM hall.lost_reason r WHERE r.property_id = p.property_id);

INSERT hall.tnc_template(property_id, code, event_type)
SELECT p.property_id, x.code, x.et FROM @props p CROSS JOIN (VALUES ('T-GEN', 'GENERAL'), ('T-WEDDING', 'WEDDING'), ('T-CORPORATE', 'CORPORATE')) x(code, et)
WHERE NOT EXISTS (SELECT 1 FROM hall.tnc_template t WHERE t.property_id = p.property_id);
INSERT hall.tnc_template_version(template_id, v, effective_from, created_by, created_utc)
SELECT t.template_id, 1, '2026-04-01', 0, SYSUTCDATETIME() FROM hall.tnc_template t JOIN @props p ON p.property_id = t.property_id
WHERE NOT EXISTS (SELECT 1 FROM hall.tnc_template_version x WHERE x.template_id = t.template_id);
INSERT hall.tnc_clause(template_id, v, n, heading, body)
SELECT t.template_id, 1, h.n, h.heading, N'OWNER TO REPLACE' FROM hall.tnc_template t JOIN @props p ON p.property_id = t.property_id
CROSS JOIN (VALUES (1, N'Advance and payment'), (2, N'Cancellation slabs'), (3, N'Guaranteed pax and extra plates'), (4, N'Timings and overtime'), (5, N'Damages'),
                   (6, N'Outside caterers and decorators'), (7, N'Music and noise limits'), (8, N'Liquor and licences (customer’s responsibility)'), (9, N'GST note'),
                   (10, N'Force majeure')) h(n, heading)
WHERE NOT EXISTS (SELECT 1 FROM hall.tnc_clause c WHERE c.template_id = t.template_id);

INSERT hall.menu_package(property_id, code, name, per_plate_paise, veg)
SELECT p.property_id, x.code, x.name, x.rate, x.veg FROM @props p
CROSS JOIN (VALUES ('MP-SILVER-VEG', N'Silver Veg', 65000, 1), ('MP-GOLD-VEG', N'Gold Veg', 85000, 1), ('MP-GOLD-NV', N'Gold Non-veg', 95000, 0)) x(code, name, rate, veg)
WHERE NOT EXISTS (SELECT 1 FROM hall.menu_package m WHERE m.property_id = p.property_id);
