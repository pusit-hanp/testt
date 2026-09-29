using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.IO;
using Dapper;
using Oracle.ManagedDataAccess.Client;

namespace Z02JHVPService
{
    public class HvpRecord
    {
        public string Material { get; set; }
        public string SpecialControlFlag { get; set; }
        public string Plant { get; set; }
        public string StorageLocation { get; set; }
        public string MaterialType { get; set; }
        public string OldMaterialNo { get; set; }
        public string MaterialDescription { get; set; }
        public string MaterialGroup { get; set; }
        public string BaseUnit { get; set; }
        public string BasicMaterial { get; set; }
        public string PurchasingGroup { get; set; }
    }

    public class HvpService
    {
        private readonly string _connectionString;

        public HvpService()
        {
            var settings = ConfigurationManager.ConnectionStrings["Material"];
            _connectionString = settings == null ? null : settings.ConnectionString;
            if (string.IsNullOrWhiteSpace(_connectionString))
            {
                HvpLog.Write("DB CONFIG FAILED: set connectionStrings/Material in Z02JHVPService.exe.config.");
                throw new InvalidOperationException("The Material connection string is missing or empty.");
            }
        }

        public int UpdateSpecialControlFlags(List<HvpRecord> records)
        {
            if (records == null || records.Count == 0) return 0;

            int processedRecords = 0;
            using (var connection = new OracleConnection(_connectionString))
            {
                connection.Open();
                string updateSql = @"UPDATE MAT_MASTER
                               SET SPECIAL_CONTROL_FLAG = :specialControlFlag,
                                   MODIFY_DATE = SYSDATE
                               WHERE TRIM(MATERIAL) = TRIM(:material)";
                string insertSql = @"INSERT INTO MAT_MASTER
                    (PLNT, SLOC, MATERIAL, BIN, MTYP, MATL_GROUP, OLD_MATERIAL_NO,
                     MATERIAL_DESCRIPTION, STANDARD_PRICE, BUN, PGR, BASIC_MATERIAL,
                     SMI2, SPQ, SPECIAL_CONTROL_FLAG, CREATE_DATE, MODIFY_DATE)
                    VALUES
                    (:plant, :storageLocation, :material, NULL, :materialType, :materialGroup, :oldMaterialNo,
                     :materialDescription, NULL, :baseUnit, :purchasingGroup, :basicMaterial,
                     NULL, NULL, :specialControlFlag, SYSDATE, NULL)";
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        foreach (var item in records)
                        {
                            if (item == null) throw new InvalidDataException("A material record is missing.");
                            string material = Field(item.Material, "Material", 18, true);
                            string flag = string.Equals(item.SpecialControlFlag == null ? null : item.SpecialControlFlag.Trim(),
                                "HVP", StringComparison.OrdinalIgnoreCase) ? "HVP" : null;
                            DynamicParameters updateParam = new DynamicParameters();
                            // Parameter order also follows SQL order for positional Oracle providers.
                            updateParam.Add("specialControlFlag", flag, DbType.String);
                            updateParam.Add("material", material, DbType.String);
                            if (connection.Execute(updateSql, updateParam, transaction) == 0)
                            {
                                DynamicParameters insertParam = InsertParameters(item, material, flag);
                                try
                                {
                                    connection.Execute(insertSql, insertParam, transaction);
                                }
                                catch (OracleException ex)
                                {
                                    // The SAP feed may insert the same key between our UPDATE and INSERT.
                                    // Only accept that race when the material can now actually be updated.
                                    if (ex.Number != 1 || connection.Execute(updateSql, updateParam, transaction) == 0)
                                        throw;
                                }
                            }
                            processedRecords++;
                        }
                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
            // Preserve the existing return meaning: input records, not affected DB rows.
            return processedRecords;
        }

        private static DynamicParameters InsertParameters(HvpRecord item, string material, string flag)
        {
            string plant = Field(item.Plant, "Plant", 4, true);
            string type = Field(item.MaterialType, "Material Type", 4, true).ToUpperInvariant();
            if (type != "ROH" && type != "HALB" && type != "FERT")
                throw new InvalidDataException("Insert Material Type must be ROH, HALB or FERT.");
            string location = Field(item.StorageLocation, "Storage Location", 4, false) ?? "STR9";

            // Keep bindings in the same order as the INSERT placeholders.
            DynamicParameters param = new DynamicParameters();
            param.Add("plant", plant, DbType.String);
            param.Add("storageLocation", location, DbType.String);
            param.Add("material", material, DbType.String);
            param.Add("materialType", type, DbType.String);
            param.Add("materialGroup", Field(item.MaterialGroup, "Material Group", 10, false), DbType.String);
            param.Add("oldMaterialNo", Field(item.OldMaterialNo, "Customer PN", 18, false), DbType.String);
            param.Add("materialDescription", Field(item.MaterialDescription, "Material Desc.", 255, false), DbType.String);
            param.Add("baseUnit", Field(item.BaseUnit, "Base unit of measure", 3, false), DbType.String);
            param.Add("purchasingGroup", Field(item.PurchasingGroup, "Purchasing group", 3, false), DbType.String);
            param.Add("basicMaterial", Field(item.BasicMaterial, "Basic Material", 2, false), DbType.String);
            param.Add("specialControlFlag", flag, DbType.String);
            return param;
        }

        private static string Field(string value, string name, int maxLength, bool required)
        {
            value = value == null ? null : value.Trim();
            if (string.IsNullOrEmpty(value))
            {
                if (required) throw new InvalidDataException(name + " is required.");
                return null;
            }
            if (value.Length > maxLength)
                throw new InvalidDataException(name + " exceeds the MAT_MASTER column length.");
            return value;
        }
    }
}
