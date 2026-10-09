-- Using a photo used to toggle it like a switch, replacing its image record with "0" or "1".
-- Rebuild that record from the purchase, the same way checkout writes it. Intact photos stay unchanged.
UPDATE items AS item
JOIN camera_purchases AS purchase ON purchase.item_id = item.id
JOIN camera_media AS media ON media.id = purchase.media_id
JOIN users AS owner ON owner.id = purchase.user_id
SET item.extra_data = JSON_COMPACT(JSON_OBJECT(
    'w', CONCAT('/camera/', media.id, '.png'),
    't', TIMESTAMPDIFF(SECOND, '1970-01-01', media.created_at),
    'o', owner.username,
    'oi', owner.id,
    's', media.room_id,
    'n', '',
    'm', ''))
WHERE item.extra_data NOT LIKE '{%';
