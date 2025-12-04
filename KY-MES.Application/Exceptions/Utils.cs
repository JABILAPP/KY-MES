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
            List<PanelDefect> panelDefects = new List<PanelDefect>();

            foreach (var board in spi.Board)
            {
                List<Domain.V1.DTOs.OutputModels.Defect> defectsByBoard = new List<Domain.V1.DTOs.OutputModels.Defect>();
                if (board.Result.Contains("NG"))
                {
                    foreach (var defect in board.Defects)
                    {
                        defectsByBoard.Add(new Domain.V1.DTOs.OutputModels.Defect
                        {
                            //Original
                            //defectId = "",
                            //defectName = defect.Defect,
                            //defectCRD = defect.Comp
                            defectId = "",
                            defectName = defect.Defect,
                            defectCRD = defect.Comp,
                            defectComment = defect.Comp
                        });
                    }
                    var matchingWipId = (from panelWips
                                         in getWip.Panel.PanelWips
                                         where board.Array == panelWips.PanelPosition
                                         select panelWips.WipId).FirstOrDefault().GetValueOrDefault();

                    panelDefects.Add(new PanelDefect
                    {
                        wipId = matchingWipId,
                        defects = defectsByBoard,
                        hasValidNumericField = true // Assuming no numeric fields are present
                    });
                }
            }

            return new AddDefectRequestModel
            {
                wipId = getWip.WipId,
                defects = [],
                hasValidNumericField = true, // Assuming no numeric fields are present
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


        //public CompleteWipPassRequestModel ToCompleteWipPass(SPIInputModel spi, GetWipIdBySerialNumberResponseModels getWip)
        //{
        //    return new CompleteWipPassRequestModel
        //    {
        //        SerialNumber = spi.Inspection.Barcode
        //    };
        //}


        public CompleteWipPassRequestModel ToCompleteWipPass(
            SPIInputModel spi,
            GetWipIdBySerialNumberResponseModels getWip)
        {
            return new CompleteWipPassRequestModel
            {
                WipId = getWip.WipId,
                Measurements = new List<MeasurementModel>
                {
                    new MeasurementModel
                    {
                        MeasurementLabel = "Carrier",
                        MeasurementData  = spi?.Pallet ?? "N/A",   // data
                        MeasureStatus = "Pass"
                    }
                }
            };
        }















    }
}
