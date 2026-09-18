using ACA360.Core.Models;
using ACA360.Logging.Interfaces;
using ACA360.Web.Controllers;
using ACA360.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ACA360.BusinessLogic.Interfaces;

namespace ACA360.Tests.Controllers
{
    public class EmployerControllerTests
    {
        private readonly Mock<IEmployerService> _mockEmployerService;
        private readonly Mock<ILoggerService> _mockLoggerService;
        private readonly EmployerController _controller;

        public EmployerControllerTests()
        {
            _mockEmployerService = new Mock<IEmployerService>();
            _mockLoggerService = new Mock<ILoggerService>(); // add this

            //_controller = new EmployerController(_mockEmployerService.Object, _mockLoggerService.Object);
        }

       

    }
}
