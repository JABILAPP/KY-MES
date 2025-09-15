using KY_MES.Domain.V1.DTOs.InputModels;
using KY_MES.Domain.V1.DTOs.OutputModels;
using System.Collections.Generic;

namespace KY_MES.Application.Utils
{
    public class Utils
    {
        public SignInRequestModel SignInRequest(string username, string password)
        {
            return new SignInRequestModel
            {
                Username = username,
                Password = password
            };
        }
        public GetWipIdBySerialNumberRequestModel SpiToGetWip(SPIInputModel spi)
        {
            return new GetWipIdBySerialNumberRequestModel
            {
                SiteName = "Manaus",
                SerialNumber = spi.Inspection.Barcode
            };
        }

        public OkToStartRequestModel ToOkToStart(SPIInputModel spi, GetWipIdBySerialNumberResponseModels getWip)
        {
            return new OkToStartRequestModel
            {
                WipId = getWip.WipId,
                ResourceName = spi.Inspection.Machine,
            };
        }

        public StartWipRequestModel ToStartWip(SPIInputModel spi, GetWipIdBySerialNumberResponseModels getWip)
        {
            return new StartWipRequestModel
            {
                WipId = getWip.WipId,
                SerialNumber = spi.Inspection.Barcode,
                ResourceName = spi.Inspection.Machine,
                StartDateTimeString = ""
            };
        }

        public CompleteWipFailRequestModel ToCompleteWipFail(SPIInputModel spi, GetWipIdBySerialNumberResponseModels getWip)
        {

            List<Failure> failures = [];
            List<PanelFailureLabelList> panelFailureLabels = [];

            foreach (var board in spi.Board)
            {
                if (board.Result.Contains("NG"))
                {
                    List<FailureLabelList> failureLabels = new List<FailureLabelList>();
                    HashSet<string> existingLabels = new HashSet<string>();

                    foreach (var defect in board.Defects)
                    {
                        if (!existingLabels.Contains(defect.Review))
                        {
                            failureLabels.Add(new FailureLabelList
                            {
                                SymptomLabel = defect.Review,
                                FailureMessage = defect.Review
                            });
                            existingLabels.Add(defect.Review);
                        }
                    }
                    var matchingWipId = (from panelWips
                                         in getWip.Panel.PanelWips
                                         where board.Array == panelWips.PanelPosition
                                         select panelWips.WipId).FirstOrDefault().GetValueOrDefault();

                    panelFailureLabels.Add(new PanelFailureLabelList
                    {
                        WipId = matchingWipId,
                        FailureLabelList = failureLabels,
                        FailureDateTime = null
                    });
                }
            }

            failures.Add(new Failure
            {
                SymptomLabel = panelFailureLabels.FirstOrDefault().FailureLabelList.FirstOrDefault().SymptomLabel,
                FailureMessage = panelFailureLabels.FirstOrDefault().FailureLabelList.FirstOrDefault().FailureMessage,
                PanelFailureLabelList = panelFailureLabels,
            });

            return new CompleteWipFailRequestModel
            {
                IsSingleWipMode = false,
                Failures = failures
            };
        }

        public AddDefectRequestModel ToAddDefect(SPIInputModel spi, GetWipIdBySerialNumberResponseModels getWip)
        {
            
            var defectMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["UNUSED"] = "UNUSED",
                ["GOOD"] = "GOOD",
                ["PASS"] = "GOOD",
                ["BADMARK"] = "BADMARK",

                ["WARNING_EXCESSIVE_VOLUME"] = "Excess solder",
                ["WARNING_INSUFFICIENT_VOLUME"] = "Insuff solder",
                ["WARNING_POSITION"] = "Solder Paste Offset",
                ["WARNING_BRIDGING"] = "Short/Bridging",
                ["WARNING_GOLDTAB"] = "GOLD SURFACE CONTACT AREA PROBLEM",
                ["WARNING_SHAPE"] = "Incorrect Shape",
                ["WARNING_UPPER_HEIGHT"] = "Solder Paste Upper Height",
                ["WARNING_LOW_HEIGHT"] = "Solder Paste Low Height",
                ["WARNING_HIGH_AREA"] = "High Area",
                ["WARNING_LOW_AREA"] = "Low Area",
                ["WARNING_COPLANARITY"] = "Coplanarity",
                ["WARNING_SMEAR"] = "Disturbed solder",
                ["WARNING_FM"] = "SOLDER COVERAGE",
                ["WARNING_SURFACE"] = "SOLDER COVERAGE",

                ["NORMALIZE_HEIGHT"] = "SOLDER COVERAGE",
                ["ROI_NUMBER"] = "SOLDER COVERAGE",

                ["EXCESSIVE_VOLUME"] = "Excess solder",
                ["INSUFFICIENT_VOLUME"] = "Insuff solder",
                ["POSITION"] = "Solder Paste Offset",
                ["BRIDGING"] = "Short/Bridging",
                ["GOLDTAB"] = "GOLD SURFACE CONTACT AREA PROBLEM",
                ["SHAPE"] = "Incorrect Shape",
                ["UPPER_HEIGHT"] = "Solder Paste Upper Height",
                ["LOW_HEIGHT"] = "Solder Paste Low Height",
                ["HIGH_AREA"] = "High Area",
                ["LOW_AREA"] = "Low Area",
                ["COPLANARITY"] = "Coplanarity",
                ["SMEAR"] = "Disturbed solder",
                ["FM"] = "SOLDER COVERAGE",
                ["SURFACE"] = "SOLDER COVERAGE"
                
            };

            List<PanelDefect> panelDefects = new List<PanelDefect>();

            foreach (var board in spi.Board)
            {
                var defectsByBoard = new List<Domain.V1.DTOs.OutputModels.Defect>();

                if (board.Result != null && board.Result.Contains("NG", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var defect in board.Defects)
                    {
                        // se existir no dicionário, troca pelo valor mapeado
                        var originalName = defect.Defect ?? string.Empty;
                        var mappedName = defectMap.TryGetValue(originalName, out var rightValue)
                                        ? rightValue
                                        : originalName;

                        defectsByBoard.Add(new Domain.V1.DTOs.OutputModels.Defect
                        {
                            defectId = "",
                            defectName = mappedName,
                            defectCRD = defect.Comp
                        });
                    }

                    var matchingWipId =
                        (from panelWips in getWip.Panel.PanelWips
                        where board.Array == panelWips.PanelPosition
                        select panelWips.WipId).FirstOrDefault().GetValueOrDefault();

                    panelDefects.Add(new PanelDefect
                    {
                        wipId = matchingWipId,
                        defects = defectsByBoard,
                        hasValidNumericField = true
                    });
                }
            }

            return new AddDefectRequestModel
            {
                wipId = getWip.WipId,
                defects = [],
                hasValidNumericField = true,
                panelDefects = panelDefects
            };
        }

        public async Task<CompleteWipResponseModel> AddDefectToCompleteWip(Task<AddDefectResponseModel> addDefectResponseTask)
        {
            //var addDefectResponseAwiated = await addDefectResponseTask;
            //var addDefectResponse = addDefectResponseAwiated ?? throw new Exception("AddDefectResponse is null");

            //return new CompleteWipResponseModel
            //{
            //    WipInQueueRouteSteps = new List<WipInQueueRouteStep>
            //    {
            //        new WipInQueueRouteStep
            //        {
            //            SerialNumber = addDefectResponse.Id.ToString(),
            //            InQueueRouteStep = new List<InQueueRouteStep>
            //            {
            //                new InQueueRouteStep
            //                {
            //                    RouteStepId = addDefectResponse.Id,
            //                    RouteStepName = addDefectResponse.MaterialName
            //                }
            //            }
            //        }
            //    },
            //    ResponseMessages = new List<string>
            //    {
            //        addDefectResponse.Status,
            //        addDefectResponse.PassStatus
            //    },
            //    Document = new Document
            //    {
            //        Model = new List<object> { addDefectResponse.MaterialName },
            //        ErrorMessage = addDefectResponse.Status
            //    }
            //};

            return new CompleteWipResponseModel();
        }


        public CompleteWipPassRequestModel ToCompleteWipPass(SPIInputModel spi, GetWipIdBySerialNumberResponseModels getWip)
        {
            return new CompleteWipPassRequestModel
            {
                SerialNumber = spi.Inspection.Barcode
            };
        }



 






    }
}
