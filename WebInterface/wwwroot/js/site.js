// Write your JavaScript code.
var app = angular.module('CBA', ['ngRoute','ui.bootstrap']);
app.run(function ($rootScope, $location) {
    $rootScope.$on("$locationChangeStart", function (event, next, current) {
        if ($location.path() !== "/" && $location.path() !== "") {
            if ($rootScope.pc === undefined) {
                window.alert("Please enter cluster connection endpoint details");
                $location.path("/");
            }
        }

        if ($location.path() === "/Status" && $rootScope.appsConfigured.length === 0) {
            window.alert("No applications Configured for backup");
            $location.path("/Applications");
        }
        
    });
    $rootScope.appsConfigured = [];
});

app.config(function ($routeProvider) {
    $routeProvider

        .when('/', {
            templateUrl: 'HomePage',
            controller: 'CBAController'
        })

        .when('/Policies', {
            templateUrl: 'Policies',
            controller: 'CBAController'
        })

        .when('/Applications', {
            templateUrl: 'Applications',
            controller: 'CBAController'
        })

        .when('/Status', {
            templateUrl: 'Status',
            controller: 'CBAController'
        })

        .otherwise({ redirectTo: '/' });
});
app.controller('CBAController', ['$rootScope', '$scope', '$http', '$timeout', '$location','$uibModal', function ($rootScope, $scope, $http, $timeout, $location, $uibModal) {

    $scope.showForm = false;
    $scope.policy = {};

    $scope.openModal = function (app) {
        $scope.appToConfigure = app;
        $scope.modalInstance = $uibModal.open({
            templateUrl: 'Modal',
            scope: $scope
        });
    };

    $scope.configure = function () {
        if ($rootScope.appsConfigured.indexOf($scope.appToConfigure) !== -1) {
            window.alert("Application already configured");
            $scope.modalInstance.dismiss('cancel');
            return;
        }
        $rootScope.appsConfigured.push($scope.appToConfigure);
        $http.post('api/RestoreService/configure/' + $scope.appToConfigure + '/' + $rootScope.pc + '/' + $scope.sc + '/' + $rootScope.hp + '/' + $rootScope.tp)
            .then(function (data, status) {
                console.log("Succesfully configured");
                window.alert("Application Successfully configured");
            }, function (data, status) {
                $scope.apps = undefined;
            });
        $rootScope.sc = $scope.sc;
        console.log("Sc in RootScope :" + $rootScope.sc);
        $scope.modalInstance.close();
    };

    $scope.cancel = function () {
        $scope.modalInstance.dismiss('cancel');
    };


    $scope.openStatusModal = function (configuredApp) {
        $http.get('api/RestoreService/status/' + $rootScope.pc + '/' + $scope.hp + '/' + $scope.tp + '/' + configuredApp)
            .then(function (data, status) {
                $uibModal.open({
                    templateUrl: 'StatusModal',
                    controller: function ($scope, $rootScope, $uibModalInstance) {
                        $scope.configuredApp = configuredApp;
                        $scope.partitionsStatus = data.data;
                        $scope.cancel = function () {
                            $uibModalInstance.dismiss('cancel');
                        };
                        $scope.Ok = function () {
                            $uibModalInstance.dismiss();
                        };
                    },
                    size : 'lg'
                });
            }, function (data, status) {
                console.log("Not happening");
            });
    };

    $scope.refresh = function () {
        $http.get('api/Home?c=' + new Date().getTime())
            .then(function (data, status) {
                $scope.votes = data;
            }, function (data, status) {
                $scope.votes = undefined;
            });
    };

    $scope.getapps = function (cs) {
//        var cs = $scope.getQueryParameterByName('cs');
        if (cs.includes("http://"))
            cs = cs.replace("http://", "");
        if (cs.includes("https://"))
            cs = cs.replace("https://", "");
        $http.get('api/RestoreService/' + cs)
            .then(function (data, status) {
                $scope.apps = data;
                console.log("Done reading");
                console.log(data);
                console.log($scope.apps.data[0]);
            }, function (data, status) {
                $scope.apps = undefined;
            });
    };

    $scope.displayForm = function () {
        $scope.showForm = true;
    };

    $scope.getpolicies = function () {
//        var cs = $scope.getQueryParameterByName('cs');
        $http.get('api/RestoreService/policies/' + $scope.pc + ':' + $scope.hp)
            .then(function (data, status) {
                $scope.policies = data;
                console.log("Policies read");
                console.log($scope.policies.data[0]);
            }, function (data, status) {
                console.log("Sad life");
                $scope.policies = undefined;
            });
    };

    $scope.openPolicyModal = function (policy) {
        $uibModal.open({
            templateUrl: 'PolicyModal',
            controller: function ($scope, $rootScope, $uibModalInstance) {
                $scope.policy = {};
                $scope.submitstoragedetails = function () {
                    var content = JSON.stringify($scope.policy);
                    $http.post('api/RestoreService/add/' + policy, content)
                        .then(function (data, status) {
                            console.log("Submitted");
                            window.alert("Storage Deatils succesfully submitted");
                        }, function (data, status) {
                            //$scope.apps = undefined;
                        });
                    $uibModalInstance.close();
                };

                $scope.cancel = function () {
                    $uibModalInstance.dismiss('cancel');
                };
            }
        });
    };

    $scope.submit = function () {
        $rootScope.pc = $scope.pc;
        $rootScope.tp = $scope.tp;
        $rootScope.hp = $scope.hp;
        console.log($rootScope.pc);
        var path = '/Policies';
        $location.path(path);
    };


    $scope.redirect = function () {
        var path = '/About?cs=';
        var address = path.concat($scope.pc + ':' + $scope.hp);
        window.location.href = address;
    };

    $scope.remove = function (item) {
        $http.delete('api/Votes/' + item)
            .then(function (data, status) {
                $scope.refresh();
            });
    };

    $scope.getQueryParameterByName = function (name) {

        name = name.replace(/[\[]/, "\\[").replace(/[\]]/, "\\]");

        var regex = new RegExp("[\\?&]" + name + "=([^&#]*)"), results = regex.exec(location.search);

        return results === null ? "" : decodeURIComponent(results[1].replace(/\+/g, " "));
    };

    $scope.send = function (cs) {
        /*
        var fd = new FormData();
        fd.append('item', item);
        $http.put('api/Votes/' + item, fd, {
            transformRequest: angular.identity,
            headers: { 'Content-Type': undefined }
        })
            .then(function (data, status) {
                $scope.refresh();
                $scope.item = undefined;
            })
    };*/
        console.log("You entered : " + cs);
    };
}]);