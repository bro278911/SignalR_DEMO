USE SignalR;
GO

CREATE TRIGGER TR_ChargingStorage_Changes 
ON ChargingStorage 
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    INSERT INTO ChargingStorageLog (ChangeType, ChangeDate)
    VALUES ('Change', GETDATE());
END;
