CREATE TRIGGER TR_ScaleBarcodeProfiles_OnlyOne
BEFORE INSERT ON ScaleBarcodeProfiles
WHEN EXISTS (SELECT 1 FROM ScaleBarcodeProfiles)
BEGIN
    SELECT RAISE(ABORT, 'scale_profile_exists');
END;
